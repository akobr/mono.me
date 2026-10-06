using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding.Object;

/// <summary>
/// Resolves <c>@</c> strings and <c>$binding</c> envelopes on a configuration object.
/// <c>$context</c> is resolved before the engine runs. <c>$definition</c> is the program and is passed through.
/// </summary>
public sealed class ConfigurationBindingResolver : IConfigurationBindingResolver
{
    public const int MaxEnvelopeDepth = 32;

    private const string BindingProperty = "$binding";
    private const string DefinitionProperty = "$definition";
    private const string ContextProperty = "$context";

    private readonly IBindingExecutor _stringBindings;

    public ConfigurationBindingResolver(IBindingExecutor stringBindings, ILogger<ConfigurationBindingResolver>? logger = null)
    {
        _stringBindings = stringBindings ?? throw new ArgumentNullException(nameof(stringBindings));
        ObjectBindingEngine.UseLogger(logger);
    }

    public async ValueTask ResolveAsync(JObject content, bool includeSecrets, BindingScope scope)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(scope);
        await ResolveTokenAsync(content, includeSecrets, scope, depth: 0, isRoot: true);
    }

    private async ValueTask<JToken> ResolveTokenAsync(
        JToken token,
        bool includeSecrets,
        BindingScope scope,
        int depth,
        bool isRoot)
    {
        switch (token)
        {
            case JValue value when value.Type == JTokenType.String:
                return await BindStringAsync(value, includeSecrets, scope);

            case JArray array:
            {
                for (var index = 0; index < array.Count; index++)
                {
                    array[index] = await ResolveTokenAsync(array[index], includeSecrets, scope, depth, isRoot: false);
                }

                return array;
            }

            case JObject obj:
                return await ResolveObjectAsync(obj, includeSecrets, scope, depth, isRoot);

            default:
                return token;
        }
    }

    private async ValueTask<JToken> ResolveObjectAsync(
        JObject obj,
        bool includeSecrets,
        BindingScope scope,
        int depth,
        bool isRoot)
    {
        if (TryReadEnvelope(obj, out var kind, out var definition, out var context, out var error))
        {
            var path = DisplayPath(obj);
            if (error is not null)
            {
                throw new BindingEvaluationException($"Failed to process the object binding for '{path}': {error}");
            }

            if (depth >= MaxEnvelopeDepth)
            {
                throw new BindingEvaluationException(
                    $"Failed to process the object binding for '{path}': nested object bindings exceed {MaxEnvelopeDepth}.");
            }

            var resolvedContext = await ResolveContextAsync(context, includeSecrets, scope, depth + 1, path);
            JToken result;
            try
            {
                result = ObjectBindingEngine.Evaluate(kind!, definition!, resolvedContext);
            }
            catch (BindingException exception)
            {
                throw new BindingException(
                    $"Failed to process the object binding for '{path}': {exception.Message}",
                    exception);
            }

            if (isRoot)
            {
                if (result is not JObject resultObject)
                {
                    throw new BindingEvaluationException(
                        $"Failed to process the object binding for '{path}': the root configuration binding must evaluate to a JSON object.");
                }

                ReplaceContents(obj, resultObject);
                return await ResolveTokenAsync(obj, includeSecrets, scope, depth + 1, isRoot: true);
            }

            return await ResolveTokenAsync(result, includeSecrets, scope, depth + 1, isRoot: false);
        }

        foreach (var property in obj.Properties().ToList())
        {
            property.Value = await ResolveTokenAsync(property.Value, includeSecrets, scope, depth, isRoot: false);
        }

        return obj;
    }

    private async ValueTask<JObject> ResolveContextAsync(
        JToken? context,
        bool includeSecrets,
        BindingScope scope,
        int depth,
        string path)
    {
        if (context is null)
        {
            return new JObject();
        }

        var resolved = await ResolveTokenAsync(context, includeSecrets, scope, depth, isRoot: false);
        if (resolved is not JObject resolvedObject)
        {
            throw new BindingEvaluationException(
                $"Failed to process the object binding for '{path}': $context must resolve to a JSON object.");
        }

        return resolvedObject;
    }

    private async ValueTask<JToken> BindStringAsync(JValue value, bool includeSecrets, BindingScope scope)
    {
        var raw = value.Value<string>();
        if (string.IsNullOrEmpty(raw) || raw[0] != '@')
        {
            return value;
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

        var binding = obj.Property(BindingProperty);
        if (binding is null || binding.Value.Type != JTokenType.String)
        {
            return false;
        }

        kind = binding.Value.Value<string>();
        var definitionProperty = obj.Property(DefinitionProperty);
        var known = kind is ObjectBindingEngine.JsonLogicKind or ObjectBindingEngine.JsonEKind;
        if (!known && definitionProperty is null)
        {
            return false;
        }

        if (!known)
        {
            error = $"Unknown object binding '{kind}'.";
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
                error = $"Unexpected property '{property.Name}' on an object binding.";
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
}
