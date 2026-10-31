using System;
using System.Threading;
using System.Threading.Tasks;
using NJsonSchema;
using NJsonSchema.References;

namespace _42.Platform.Storyteller.Configuring;

// Resolves the references inside the schema document only, as the MCP host's resolver does. A schema is data written by
// a project member, so a $ref to a URL or a file must not make the API fetch the URL or read the file. NJsonSchema
// resolves '#' and '#/…' from the document itself and sends any other $ref here: one that starts with http:// or
// https:// to ResolveUrlReferenceAsync, everything else (with the document path) to ResolveFilePath.
internal sealed class LocalReferenceResolver(JsonSchemaAppender appender) : JsonReferenceResolver(appender)
{
    // The document path a schema is parsed with, so that a relative or scheme-less $ref reaches ResolveFilePath (and is
    // refused) instead of failing because no document path is available.
    public const string DocumentPath = "/configuration-schema.json";

    public static Func<JsonSchema, JsonReferenceResolver> Factory { get; } =
        schema => new LocalReferenceResolver(new JsonSchemaAppender(schema, new DefaultTypeNameGenerator()));

    // Before any path is built, so the refusal quotes the $ref as written instead of a path of the API host.
    public override string ResolveFilePath(string documentPath, string jsonPath)
    {
        if (jsonPath.StartsWith('#'))
        {
            // a plain-name fragment (#foo) or a pointer without its slash (#definitions/a): inside the document, but
            // NJsonSchema resolves only '#' and JSON Pointers
            throw new NotSupportedException(
                $"The $ref '{ConfigurationSchemaChecker.Shorten(jsonPath)}' is not supported; a reference inside the schema is '#' or a JSON Pointer ('#/…').");
        }

        throw new ExternalSchemaReferenceException(jsonPath);
    }

    // Not reached while ResolveFilePath refuses first; the file name only, never a path of the API host.
    public override Task<IJsonReference> ResolveFileReferenceAsync(string filePath, CancellationToken cancellationToken = default)
    {
        throw new ExternalSchemaReferenceException(filePath[(filePath.LastIndexOfAny(['/', '\\']) + 1)..]);
    }

    public override Task<IJsonReference> ResolveUrlReferenceAsync(string url, CancellationToken cancellationToken = default)
    {
        throw new ExternalSchemaReferenceException(url);
    }
}
