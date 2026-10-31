using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding.Object;

/// <summary>
/// Resolves <c>@</c> strings and <c>$binding</c> envelopes on a configuration object.
/// <c>$context</c> is resolved before the engine runs. <c>$definition</c> is the program and is passed through.
/// One <see cref="EvaluationMeter"/> bounds the work of all envelopes in one read (see <see cref="ObjectBindingLimits"/>).
/// Every string binding gets the same scope, so its reads record into one <see cref="BindingScope.Dependencies"/>.
/// JSON-e can read the clock, and a string executor that does not record hides its reads; both make the manifest volatile.
/// The result of an envelope is walked again, so it can hold more bindings. Their paths and texts are computed and can carry
/// an evaluated value, even a secret: a failure inside a result is reported at the path of the stored envelope that produced it,
/// with a fixed message.
/// </summary>
public sealed class ConfigurationBindingResolver : IConfigurationBindingResolver, IRecordingBinding
{
    private const string BindingProperty = "$binding";
    private const string DefinitionProperty = "$definition";
    private const string ContextProperty = "$context";

    private readonly IBindingExecutor _stringBindings;
    private readonly ILogger<ConfigurationBindingResolver>? _logger;
    private readonly ObjectBindingLimits _limits;
    private readonly TimeProvider _timeProvider;

    public ConfigurationBindingResolver(
        IBindingExecutor stringBindings,
        ILogger<ConfigurationBindingResolver>? logger = null,
        ObjectBindingLimits? limits = null,
        TimeProvider? timeProvider = null)
    {
        _stringBindings = stringBindings ?? throw new ArgumentNullException(nameof(stringBindings));
        _logger = logger;
        _limits = limits ?? ObjectBindingLimits.Default;
        _limits.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
        if (logger is not null)
        {
            ObjectBindingEngine.UseLogger(logger);
        }
    }

    public async ValueTask ResolveAsync(JObject content, bool includeSecrets, BindingScope scope)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(scope);
        var budget = new EvaluationBudget(_limits, _timeProvider);
        await ResolveTokenAsync(content, includeSecrets, scope, depth: 0, isRoot: true, budget, emittedBy: null);

        if (budget.Count > 0 && _logger?.IsEnabled(LogLevel.Debug) == true)
        {
            _logger.LogDebug(
                "Object bindings of {Configuration} evaluated {Envelopes} envelopes in {Steps} steps, {ElapsedMs} ms, {AllocatedBytes} bytes.",
                scope.Context,
                budget.Count,
                budget.Meter.Steps,
                budget.Meter.Elapsed.TotalMilliseconds,
                budget.Meter.AllocatedBytes);
        }
    }

    // A string that starts with '@' or an object with a string $binding; the walk below changes nothing else.
    // A $binding without a known kind and $definition stays as stored, but it counts, so the check errs towards resolving.
    public bool ContainsBindings(JToken content)
    {
        ArgumentNullException.ThrowIfNull(content);

        switch (content)
        {
            case JValue value when value.Type == JTokenType.String:
                return IsBindingString(value.Value<string>());

            case JArray array:
                return array.Any(ContainsBindings);

            case JObject obj:
                return HasBindingProperty(obj) || obj.Properties().Any(property => ContainsBindings(property.Value));

            default:
                return false;
        }
    }

    private static bool IsBindingString(string? raw)
    {
        return !string.IsNullOrEmpty(raw) && raw[0] == '@';
    }

    private static bool HasBindingProperty(JObject obj)
    {
        return obj.Property(BindingProperty)?.Value.Type == JTokenType.String;
    }

    // emittedBy is the path of the stored envelope whose result holds the token, or null for the stored document itself
    private async ValueTask<JToken> ResolveTokenAsync(
        JToken token,
        bool includeSecrets,
        BindingScope scope,
        int depth,
        bool isRoot,
        EvaluationBudget budget,
        string? emittedBy)
    {
        switch (token)
        {
            case JValue value when value.Type == JTokenType.String:
                return await BindStringAsync(value, includeSecrets, scope, emittedBy);

            case JArray array:
            {
                for (var index = 0; index < array.Count; index++)
                {
                    array[index] = await ResolveTokenAsync(array[index], includeSecrets, scope, depth, isRoot: false, budget, emittedBy);
                }

                return array;
            }

            case JObject obj:
                return await ResolveObjectAsync(obj, includeSecrets, scope, depth, isRoot, budget, emittedBy);

            default:
                return token;
        }
    }

    private async ValueTask<JToken> ResolveObjectAsync(
        JObject obj,
        bool includeSecrets,
        BindingScope scope,
        int depth,
        bool isRoot,
        EvaluationBudget budget,
        string? emittedBy)
    {
        if (TryReadEnvelope(obj, out var kind, out var definition, out var context, out var error))
        {
            var path = emittedBy ?? DisplayPath(obj);
            if (error is not null)
            {
                throw new BindingEvaluationException(
                    $"Failed to process the object binding for '{path}': {error}",
                    path,
                    innerException: null);
            }

            if (depth >= _limits.MaxEnvelopeDepth)
            {
                throw LimitExceeded(
                    new EvaluationLimitExceededException(
                        EvaluationLimitKind.EnvelopeDepth,
                        _limits.MaxEnvelopeDepth,
                        $"Failed to process the object binding for '{path}': nested object bindings exceed {_limits.MaxEnvelopeDepth}.",
                        path,
                        innerException: null),
                    budget,
                    scope);
            }

            var resolvedContext = await ResolveContextAsync(context, includeSecrets, scope, depth + 1, path, budget, emittedBy);
            if (budget.Count >= _limits.MaxEnvelopeEvaluations)
            {
                throw LimitExceeded(
                    new EvaluationLimitExceededException(
                        EvaluationLimitKind.EnvelopeCount,
                        _limits.MaxEnvelopeEvaluations,
                        $"Failed to process the object binding for '{path}': object bindings exceed {_limits.MaxEnvelopeEvaluations} evaluations.",
                        path,
                        innerException: null),
                    budget,
                    scope);
            }

            budget.Count++;

            // JSON-e pushes the time of the evaluation as `now`
            if (kind == ObjectBindingEngine.JsonEKind)
            {
                scope.Dependencies?.MarkVolatile("JSON-e can read the clock.");
            }

            JToken result;
            try
            {
                result = ObjectBindingEngine.Evaluate(kind!, definition!, resolvedContext, budget.Meter, emitted: emittedBy is not null);
            }
            catch (EvaluationLimitExceededException exception)
            {
                throw LimitExceeded(
                    new EvaluationLimitExceededException(
                        exception.Kind,
                        exception.Limit,
                        $"Failed to process the object binding for '{path}': {exception.Message}",
                        path,
                        exception),
                    budget,
                    scope);
            }
            catch (BindingEvaluationException exception)
            {
                throw new BindingEvaluationException(
                    $"Failed to process the object binding for '{path}': {exception.Message}",
                    path,
                    exception);
            }
            catch (BindingException exception)
            {
                throw new BindingEvaluationException(
                    $"Failed to process the object binding for '{path}': {exception.Message}",
                    path,
                    exception);
            }

            if (isRoot)
            {
                if (result is not JObject resultObject)
                {
                    throw new BindingEvaluationException(
                        $"Failed to process the object binding for '{path}': the root configuration binding must evaluate to a JSON object.",
                        path,
                        innerException: null);
                }

                ReplaceContents(obj, resultObject);
                return await ResolveTokenAsync(obj, includeSecrets, scope, depth + 1, isRoot: true, budget, path);
            }

            return await ResolveTokenAsync(result, includeSecrets, scope, depth + 1, isRoot: false, budget, path);
        }

        foreach (var property in obj.Properties().ToList())
        {
            property.Value = await ResolveTokenAsync(property.Value, includeSecrets, scope, depth, isRoot: false, budget, emittedBy);
        }

        return obj;
    }

    private async ValueTask<JObject> ResolveContextAsync(
        JToken? context,
        bool includeSecrets,
        BindingScope scope,
        int depth,
        string path,
        EvaluationBudget budget,
        string? emittedBy)
    {
        if (context is null)
        {
            return new JObject();
        }

        var resolved = await ResolveTokenAsync(context, includeSecrets, scope, depth, isRoot: false, budget, emittedBy);
        if (resolved is not JObject resolvedObject)
        {
            throw new BindingEvaluationException(
                $"Failed to process the object binding for '{path}': $context must resolve to a JSON object.",
                path,
                innerException: null);
        }

        return resolvedObject;
    }

    private async ValueTask<JToken> BindStringAsync(JValue value, bool includeSecrets, BindingScope scope, string? emittedBy)
    {
        if (emittedBy is null)
        {
            return await BindStringAsync(value, includeSecrets, scope);
        }

        // A string in a result: its JSON path and its text are computed, so neither may reach the message or the log.
        try
        {
            return await BindStringAsync(value, includeSecrets, scope);
        }
        catch (EvaluationLimitExceededException exception)
        {
            throw new EvaluationLimitExceededException(
                exception.Kind,
                exception.Limit,
                $"Failed to process the object binding for '{emittedBy}': a data binding in its result exceeds the {exception.Kind} limit of {exception.Limit}.",
                emittedBy,
                exception);
        }
        catch (BindingEvaluationException exception)
        {
            throw new BindingEvaluationException(
                $"Failed to process the object binding for '{emittedBy}': a data binding in its result cannot be evaluated.",
                emittedBy,
                exception);
        }
    }

    private async ValueTask<JToken> BindStringAsync(JValue value, bool includeSecrets, BindingScope scope)
    {
        if (!IsBindingString(value.Value<string>()))
        {
            return value;
        }

        if (_stringBindings is not IRecordingBinding)
        {
            scope.Dependencies?.MarkVolatile("The string bindings do not record their reads.");
        }

        var parent = value.Parent;
        if (parent is JProperty)
        {
            await _stringBindings.TryBinding(value, includeSecrets, scope);
            return ((JProperty)parent).Value;
        }

        if (parent is JArray array)
        {
            var index = array.IndexOf(value);
            await _stringBindings.TryBinding(value, includeSecrets, scope);
            return array[index];
        }

        var holder = new JObject { ["v"] = value };
        await _stringBindings.TryBinding((JValue)holder["v"]!, includeSecrets, scope);
        return holder["v"]!.DeepClone();
    }

    private EvaluationLimitExceededException LimitExceeded(
        EvaluationLimitExceededException exception,
        EvaluationBudget budget,
        BindingScope scope)
    {
        if (_logger?.IsEnabled(LogLevel.Warning) == true)
        {
            _logger.LogWarning(
                "Object binding limit {Kind} ({Limit}) exceeded at {Path} of {Configuration} after {Envelopes} envelopes, {Steps} steps, {ElapsedMs} ms, {AllocatedBytes} bytes.",
                exception.Kind,
                exception.Limit,
                exception.Path,
                scope.Context,
                budget.Count,
                budget.Meter.Steps,
                budget.Meter.Elapsed.TotalMilliseconds,
                budget.Meter.AllocatedBytes);
        }

        return exception;
    }

    private static bool TryReadEnvelope(
        JObject obj,
        out string? kind,
        out JToken? definition,
        out JToken? context,
        out string? error)
    {
        kind = null;
        definition = null;
        context = null;
        error = null;

        if (!HasBindingProperty(obj))
        {
            return false;
        }

        kind = obj.Property(BindingProperty)!.Value.Value<string>();
        var definitionProperty = obj.Property(DefinitionProperty);
        var known = kind is ObjectBindingEngine.JsonLogicKind or ObjectBindingEngine.JsonEKind;
        if (!known && definitionProperty is null)
        {
            return false;
        }

        if (!known)
        {
            error = $"Unknown object binding: $binding must be '{ObjectBindingEngine.JsonEKind}' or '{ObjectBindingEngine.JsonLogicKind}'.";
            return true;
        }

        if (definitionProperty is null)
        {
            error = "Object binding requires $definition.";
            return true;
        }

        foreach (var property in obj.Properties())
        {
            if (property.Name is not (BindingProperty or DefinitionProperty or ContextProperty))
            {
                error = "An object binding allows only $binding, $definition and $context.";
                return true;
            }
        }

        definition = definitionProperty.Value;
        context = obj.Property(ContextProperty)?.Value;
        return true;
    }

    private static void ReplaceContents(JObject target, JObject replacement)
    {
        target.RemoveAll();
        foreach (var property in replacement.Properties().ToList())
        {
            property.Remove();
            target.Add(property);
        }
    }

    private static string DisplayPath(JToken token)
    {
        return string.IsNullOrEmpty(token.Path) ? "$" : token.Path;
    }

    internal sealed class EvaluationBudget
    {
        public EvaluationBudget(ObjectBindingLimits limits, TimeProvider timeProvider)
        {
            Meter = new EvaluationMeter(limits, timeProvider);
        }

        public EvaluationMeter Meter { get; }

        public int Count { get; set; }
    }
}
