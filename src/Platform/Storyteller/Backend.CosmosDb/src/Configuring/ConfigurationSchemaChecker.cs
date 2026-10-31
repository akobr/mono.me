using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NJsonSchema;
using NJsonSchema.Validation;

namespace _42.Platform.Storyteller.Configuring;

// Every NJsonSchema call of CosmosConfigurationSchemaService: the parse of a schema and the check of a document against
// it. A schema is data written by a project member, so a $ref is resolved inside the schema only, and neither the parse
// nor the check may hold the worker: both have a budget (CheckBudget), a pattern has its own match timeout, and the
// work NJsonSchema does within one step is guarded (GuardedValidator). A failure is an InvalidInputException with a
// schema code (IsCheckFailure), which the API answers with 400 and an import reports for its file.
internal sealed class ConfigurationSchemaChecker(ConfigurationSchemaOptions options, TimeProvider timeProvider)
{
    private const int MaxQuotedLength = 100;

    // About half a second of NJsonSchema's comparison of an object or array with the entries of an 'enum' (in characters
    // serialized), which happens within one step of the validator.
    private const long MaxEnumerationWork = 50_000_000;

    // About half a second of NJsonSchema's search of an object for the names of 'properties' and 'required' it lacks (in
    // name comparisons), which happens within one step of the validator.
    private const long MaxPropertySearchWork = 50_000_000;

    // Far more calls of NJsonSchema's IsNullable than any schema needs for one value (a few milliseconds).
    private const int MaxNullabilityCalls = 100_000;

    // Far deeper than any chain of oneOf and references a schema needs. A fixed depth, not the stack left: NJsonSchema's
    // own walk uses more stack per level than the walk here, so a walk limited by the stack alone could pass here and
    // then overflow in NJsonSchema.
    private const int MaxNullabilityDepth = 256;

    // Far longer than any chain of references a schema needs, in schemas: a $ref, or an allOf, oneOf or anyOf of one
    // entry, points to the next, so a wrapped reference is two (about 256 levels). NJsonSchema follows such a chain
    // recursively in ActualSchema and HasReference, within one step and before any guard of the validator runs, so a
    // long one would overflow the stack.
    private const int MaxReferenceChain = 512;

    public static bool IsCheckFailure(InvalidInputException exception)
    {
        return exception.ErrorCode
            is ErrorCodes.InvalidSchema
            or ErrorCodes.ExternalSchemaReference
            or ErrorCodes.SchemaValidationTimeout;
    }

    // A reference or a pattern quoted in a message: a schema write can carry one of several MiB, and the message is
    // logged and returned.
    public static string Shorten(string value)
    {
        return value.Length <= MaxQuotedLength ? value : $"{value[..MaxQuotedLength]}…";
    }

    public async Task<JsonSchema> ParseAsync(JObject schemaContent)
    {
        // NJsonSchema observes the token once per reference: a $ref to a large map is resolved by serializing the map
        using var budget = new CancellationTokenSource(options.CheckBudget, timeProvider);

        try
        {
            var schema = await JsonSchema.FromJsonAsync(
                schemaContent.ToString(Formatting.None),
                LocalReferenceResolver.DocumentPath,
                LocalReferenceResolver.Factory,
                budget.Token);

            // before any getter of NJsonSchema follows the references
            EnsureReferenceChainsFit(schema);
            return schema;
        }
        catch (Exception exception) when (budget.IsCancellationRequested && Find<OperationCanceledException>(exception) is not null)
        {
            throw new InvalidInputException(
                $"Reading the schema takes longer than {Describe(options.CheckBudget)}; simplify its references.",
                ErrorCodes.SchemaValidationTimeout);
        }
        catch (Exception exception) when (Find<ExternalSchemaReferenceException>(exception) is { } external)
        {
            throw new InvalidInputException(
                $"External $ref not allowed: '{Shorten(external.Reference)}'. Only '#' and JSON Pointers inside the schema ('#/…') are resolved.",
                ErrorCodes.ExternalSchemaReference);
        }
        catch (Exception exception)
        {
            throw InvalidSchema(exception.Message);
        }
    }

    // "{path}: {kind}" per violation.
    public IReadOnlyList<string> Validate(JsonSchema schema, JObject content)
    {
        ICollection<ValidationError> errors;

        try
        {
            // as JsonSchema.Validate(string) does: the actual schema, and the document read back from its text, so that
            // date-like strings stay strings
            errors = new GuardedValidator(options, timeProvider).Validate(content.ToString(Formatting.None), schema.ActualSchema);
        }
        catch (RegexMatchTimeoutException exception)
        {
            throw new InvalidInputException(
                $"The pattern '{Shorten(exception.Pattern)}' of the schema took longer than {Describe(exception.MatchTimeout)} to match a value; simplify the pattern.",
                ErrorCodes.SchemaValidationTimeout);
        }
        catch (InsufficientExecutionStackException)
        {
            throw InvalidSchema("the schema nests too deeply to be checked.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or ArithmeticException)
        {
            // for example "Cyclic references detected." of a schema that is its own allOf, an invalid pattern, or a
            // multipleOf of 0 (also 1e-30, which is 0 as a decimal)
            throw InvalidSchema(exception.Message);
        }

        return errors.Select(error => $"{error.Path}: {error.Kind}").ToList();
    }

    // Every schema of the document, found without recursion, and the length of the reference chain from each, counted
    // without NJsonSchema's recursive getters. A wrapper of one entry counts even where NJsonSchema would not take it as
    // a reference, and a cycle ends a chain (NJsonSchema refuses it as "Cyclic references detected.").
    private static void EnsureReferenceChainsFit(JsonSchema root)
    {
        var schemas = new HashSet<JsonSchema>(ReferenceEqualityComparer.Instance) { root };
        var pending = new Stack<JsonSchema>([root]);

        while (pending.TryPop(out var schema))
        {
            foreach (var child in ChildrenOf(schema))
            {
                if (child is not null && schemas.Add(child))
                {
                    pending.Push(child);
                }
            }
        }

        var lengths = new Dictionary<JsonSchema, int>(ReferenceEqualityComparer.Instance);
        var chain = new List<JsonSchema>();
        var onChain = new HashSet<JsonSchema>(ReferenceEqualityComparer.Instance);

        foreach (var start in schemas)
        {
            chain.Clear();
            onChain.Clear();
            var current = start;

            while (current is not null && !lengths.ContainsKey(current) && onChain.Add(current))
            {
                chain.Add(current);
                current = NextInChain(current);
            }

            var length = current is not null && lengths.TryGetValue(current, out var known) ? known : 0;

            for (var index = chain.Count - 1; index >= 0; index--)
            {
                lengths[chain[index]] = ++length;

                if (length > MaxReferenceChain)
                {
                    throw new InvalidOperationException(
                        $"the schema chains more than {MaxReferenceChain} schemas through references ($ref, or allOf, oneOf or anyOf of one entry), which is too deep to be checked.");
                }
            }
        }
    }

    private static JsonSchema? NextInChain(JsonSchema schema)
    {
        return schema.Reference
            ?? (schema.AllOf.Count == 1 ? schema.AllOf.First()
                : schema.OneOf.Count == 1 ? schema.OneOf.First()
                : schema.AnyOf.Count == 1 ? schema.AnyOf.First()
                : null);
    }

    private static IEnumerable<JsonSchema?> ChildrenOf(JsonSchema schema)
    {
        yield return schema.Reference;
        yield return schema.Item;
        yield return schema.Not;
        yield return schema.AdditionalPropertiesSchema;
        yield return schema.AdditionalItemsSchema;

        foreach (var child in schema.Items.Concat(schema.AllOf).Concat(schema.OneOf).Concat(schema.AnyOf)
            .Concat(schema.Definitions.Values).Concat(schema.Properties.Values).Concat(schema.PatternProperties.Values))
        {
            yield return child;
        }
    }

    private static InvalidInputException InvalidSchema(string reason)
    {
        return new InvalidInputException($"Invalid JSON Schema: {reason}", ErrorCodes.InvalidSchema);
    }

    private static string Describe(TimeSpan duration)
    {
        return duration >= TimeSpan.FromSeconds(1)
            ? string.Create(CultureInfo.InvariantCulture, $"{duration.TotalSeconds:0.###} s")
            : string.Create(CultureInfo.InvariantCulture, $"{duration.TotalMilliseconds:0.###} ms");
    }

    // NJsonSchema wraps the exception of a URL reference in its own.
    private static T? Find<T>(Exception? exception)
        where T : Exception
    {
        for (; exception is not null; exception = exception.InnerException)
        {
            if (exception is T found)
            {
                return found;
            }
        }

        return null;
    }

    // NJsonSchema's validator with its default settings (the format validators included), guarded at every step: the
    // protected Validate is where it enters each value with each schema. A guard throws InvalidInputException, which
    // NJsonSchema does not catch.
    private sealed class GuardedValidator : JsonSchemaValidator
    {
        private readonly ConfigurationSchemaOptions _options;
        private readonly TimeProvider _timeProvider;
        private readonly long _started;

        // the steps being validated, outermost first
        private readonly List<(JToken Token, JsonSchema Schema)> _path = [];

        // the schemas of the current walk of IsNullable, and its calls so far
        private readonly HashSet<JsonSchema> _nullabilityPath = new(ReferenceEqualityComparer.Instance);
        private int _nullabilityCalls;

        public GuardedValidator(ConfigurationSchemaOptions options, TimeProvider timeProvider)
            : base((JsonSchemaValidatorSettings?)null)
        {
            _options = options;
            _timeProvider = timeProvider;
            _started = timeProvider.GetTimestamp();
        }

        protected override ICollection<ValidationError> Validate(JToken token, JsonSchema schema, SchemaType schemaType, string? propertyName, string propertyPath)
        {
            if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
            {
                throw new InsufficientExecutionStackException();
            }

            EnsureNoCycle(token, schema);
            EnsureWithinBudget();

            // ValidateEnum and ValidateType ask whether the step's schema is nullable
            EnsureNullabilityFits(schema);
            MatchPattern(token, schema);
            EnsureEnumerationFits(token, schema);

            if (token is JObject value)
            {
                MatchPatternProperties(value, schema);
                EnsurePropertySearchFits(value, schema);
                EnsureNullPropertiesFit(value, schema);
            }

            _path.Add((token, schema));

            try
            {
                return base.Validate(token, schema, schemaType, propertyName, propertyPath);
            }
            finally
            {
                _path.RemoveAt(_path.Count - 1);
            }
        }

        // A step depends only on its value and its schema, so meeting the same pair again below itself never ends. allOf,
        // anyOf, oneOf and not validate the same value; every other step enters a child value, so only the steps of the
        // current value are compared.
        private void EnsureNoCycle(JToken token, JsonSchema schema)
        {
            for (var index = _path.Count - 1; index >= 0 && ReferenceEquals(_path[index].Token, token); index--)
            {
                if (ReferenceEquals(_path[index].Schema, schema))
                {
                    throw Cycle();
                }
            }
        }

        private void EnsureWithinBudget()
        {
            if (_timeProvider.GetElapsedTime(_started) > _options.CheckBudget)
            {
                throw TooLong();
            }
        }

        // NJsonSchema matches a string's pattern with the process default timeout, once per type of the schema; the same
        // match here, with the explicit timeout, bounds it in any host.
        private void MatchPattern(JToken token, JsonSchema schema)
        {
            if (token.Type == JTokenType.String && !string.IsNullOrEmpty(schema.Pattern))
            {
                Regex.IsMatch(token.Value<string>()!, schema.Pattern, RegexOptions.None, _options.PatternMatchTimeout);
            }
        }

        // NJsonSchema matches each property that 'properties' does not declare against the patterns in order, up to the
        // first match, all within one step; the same matches here have the explicit timeout and the budget between them.
        private void MatchPatternProperties(JObject value, JsonSchema schema)
        {
            if (schema.PatternProperties.Count == 0)
            {
                return;
            }

            foreach (var property in value.Properties())
            {
                if (schema.Properties.ContainsKey(property.Name))
                {
                    continue;
                }

                foreach (var pattern in schema.PatternProperties.Keys)
                {
                    EnsureWithinBudget();

                    if (Regex.IsMatch(property.Name, pattern, RegexOptions.None, _options.PatternMatchTimeout))
                    {
                        break;
                    }
                }
            }
        }

        // NJsonSchema compares the value serialized (indented) with every entry, serializing it once per entry.
        private void EnsureEnumerationFits(JToken token, JsonSchema schema)
        {
            if (token is JContainer && schema.Enumeration.Count > 0
                && (long)schema.Enumeration.Count * token.ToString().Length > MaxEnumerationWork)
            {
                throw TooLong();
            }
        }

        // NJsonSchema looks up every name of 'properties' and of 'required'; for a name the object lacks, it compares the
        // name with every property of the object, all within one step.
        private void EnsurePropertySearchFits(JObject value, JsonSchema schema)
        {
            long missing = schema.Properties.Keys.Count(name => !value.ContainsKey(name))
                + schema.RequiredProperties.Count(name => !schema.Properties.ContainsKey(name) && !value.ContainsKey(name));

            if (missing * value.Count > MaxPropertySearchWork)
            {
                throw TooLong();
            }
        }

        // NJsonSchema asks whether the schema of a declared property is nullable when its value is null, within this
        // step, before the property's own step.
        private void EnsureNullPropertiesFit(JObject value, JsonSchema schema)
        {
            foreach (var (name, propertySchema) in schema.Properties)
            {
                if (value.TryGetValue(name, out var propertyValue) && propertyValue.Type == JTokenType.Null)
                {
                    EnsureNullabilityFits(propertySchema);
                }
            }
        }

        // NJsonSchema's IsNullable recurses through oneOf and through the actual (type) schema, without memoization and
        // without passing the protected Validate: a cycle there overflows the stack, and a graph of oneOf references takes
        // exponential time within one step. The same walk here, counted and with its path, refuses both; when it ends,
        // NJsonSchema's walk makes the same calls.
        private void EnsureNullabilityFits(JsonSchema schema)
        {
            _nullabilityCalls = 0;
            _nullabilityPath.Clear();
            IsNullable(schema);
        }

        // JsonSchema.IsNullable(SchemaType.JsonSchema), call for call (JsonSchemaProperty adds a rule for Swagger 2 only).
        private bool IsNullable(JsonSchema schema)
        {
            if (_nullabilityPath.Count >= MaxNullabilityDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
            {
                throw new InsufficientExecutionStackException();
            }

            if (++_nullabilityCalls > MaxNullabilityCalls)
            {
                throw TooLong();
            }

            // the call depends only on the schema, so meeting it again below itself never ends
            if (!_nullabilityPath.Add(schema))
            {
                throw Cycle();
            }

            try
            {
                // (the type test is NJsonSchema's internal JsonObjectType.IsNull)
                if (schema.IsNullableRaw == true
                    || (schema.IsEnumeration && schema.Enumeration.Contains(null))
                    || (schema.Type & JsonObjectType.Null) != 0)
                {
                    return true;
                }

                if (schema.Type == JsonObjectType.None)
                {
                    foreach (var oneOf in schema.OneOf)
                    {
                        if (IsNullable(oneOf))
                        {
                            return true;
                        }
                    }
                }

                var actualSchema = schema.ActualSchema;

                if (actualSchema != schema && IsNullable(actualSchema))
                {
                    return true;
                }

                var actualTypeSchema = ActualTypeSchema(schema);

                if (actualTypeSchema != schema && IsNullable(actualTypeSchema))
                {
                    return true;
                }

                return schema.ExtensionData is not null
                    && schema.ExtensionData.TryGetValue("nullable", out var nullable)
                    && bool.TryParse(nullable?.ToString(), out var isNullable)
                    && isNullable;
            }
            finally
            {
                _nullabilityPath.Remove(schema);
            }
        }

        // JsonSchema.ActualTypeSchema, whose oneOf branch asks IsNullable of the entries in order.
        private JsonSchema ActualTypeSchema(JsonSchema schema)
        {
            var source = schema.Reference ?? schema;

            if (source.AllOf.Count > 1 && source.AllOf.Count(static entry => !entry.HasReference && !entry.IsDictionary) == 1)
            {
                return source.AllOf.First(static entry => !entry.HasReference && !entry.IsDictionary).ActualSchema;
            }

            foreach (var oneOf in source.OneOf)
            {
                if (!IsNullable(oneOf))
                {
                    return oneOf.ActualSchema;
                }
            }

            return schema.ActualSchema;
        }

        private static InvalidInputException Cycle()
        {
            return InvalidSchema("the schema applies itself to the same value again (for example \"not\": { \"$ref\": \"#\" }), so its check would never end.");
        }

        private InvalidInputException TooLong()
        {
            return new InvalidInputException(
                $"Checking the document against its schema takes longer than {Describe(_options.CheckBudget)}; simplify the schema or the document.",
                ErrorCodes.SchemaValidationTimeout);
        }
    }
}
