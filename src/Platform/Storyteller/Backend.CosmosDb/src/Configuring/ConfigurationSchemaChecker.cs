using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NJsonSchema;
using NJsonSchema.Validation;

namespace _42.Platform.Storyteller.Configuring;

// Every NJsonSchema call of CosmosConfigurationSchemaService: the parse of a schema and the check of a document against
// it. A schema is data written by a project member, so a $ref is resolved inside the schema only. A failure is an
// InvalidInputException with a schema code (IsCheckFailure), which the API answers with 400 and an import reports for
// its file.
internal sealed class ConfigurationSchemaChecker
{
    private const int MaxQuotedLength = 100;

    public static bool IsCheckFailure(InvalidInputException exception)
    {
        return exception.ErrorCode is ErrorCodes.InvalidSchema or ErrorCodes.ExternalSchemaReference;
    }

    // A reference or a pattern quoted in a message: a schema write can carry one of several MiB, and the message is
    // logged and returned.
    public static string Shorten(string value)
    {
        return value.Length <= MaxQuotedLength ? value : $"{value[..MaxQuotedLength]}…";
    }

    public async Task<JsonSchema> ParseAsync(JObject schemaContent)
    {
        try
        {
            return await JsonSchema.FromJsonAsync(
                schemaContent.ToString(Formatting.None),
                LocalReferenceResolver.DocumentPath,
                LocalReferenceResolver.Factory);
        }
        catch (Exception exception) when (FindExternalReference(exception) is { } external)
        {
            throw new InvalidInputException(
                $"External $ref not allowed: '{Shorten(external.Reference)}'. Only '#' and JSON Pointers inside the schema ('#/…') are resolved.",
                ErrorCodes.ExternalSchemaReference);
        }
        catch (Exception exception)
        {
            throw new InvalidInputException($"Invalid JSON Schema: {exception.Message}", ErrorCodes.InvalidSchema);
        }
    }

    // "{path}: {kind}" per violation.
    public IReadOnlyList<string> Validate(JsonSchema schema, JObject content)
    {
        // as JsonSchema.Validate(string) does: the default settings (with their format validators), the actual schema,
        // and the document read back from its text, so that date-like strings stay strings
        var errors = new JsonSchemaValidator((JsonSchemaValidatorSettings?)null)
            .Validate(content.ToString(Formatting.None), schema.ActualSchema);

        return errors.Select(error => $"{error.Path}: {error.Kind}").ToList();
    }

    // NJsonSchema wraps the exception of a URL reference in its own.
    private static ExternalSchemaReferenceException? FindExternalReference(Exception? exception)
    {
        for (; exception is not null; exception = exception.InnerException)
        {
            if (exception is ExternalSchemaReferenceException external)
            {
                return external;
            }
        }

        return null;
    }
}
