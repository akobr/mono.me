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
            return await JsonSchema.FromJsonAsync(
                schemaContent.ToString(Formatting.None),
                LocalReferenceResolver.DocumentPath,
                LocalReferenceResolver.Factory,
                budget.Token);
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
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            // for example "Cyclic references detected." of a schema that is its own allOf, or an invalid pattern
            throw InvalidSchema(exception.Message);
        }

        return errors.Select(error => $"{error.Path}: {error.Kind}").ToList();
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
            MatchPattern(token, schema);
            MatchPatternProperties(token, schema);
            EnsureEnumerationFits(token, schema);

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
                    throw InvalidSchema("the schema applies itself to the same value again (for example \"not\": { \"$ref\": \"#\" }), so its check would never end.");
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
        private void MatchPatternProperties(JToken token, JsonSchema schema)
        {
            if (token is not JObject value || schema.PatternProperties.Count == 0)
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

        private InvalidInputException TooLong()
        {
            return new InvalidInputException(
                $"Checking the document against its schema takes longer than {Describe(_options.CheckBudget)}; simplify the schema or the document.",
                ErrorCodes.SchemaValidationTimeout);
        }
    }
}
