using NJsonSchema;
using NJsonSchema.References;

namespace _42.Platform.Storyteller.Mcp.Understanding;

// Resolves the references inside the schema document only. A schema is data written by a project member, so a $ref to a
// URL or a file must not make the host fetch the URL or read the file.
public sealed class LocalReferenceResolver(JsonSchemaAppender appender) : JsonReferenceResolver(appender)
{
    public static Func<JsonSchema, JsonReferenceResolver> Factory { get; } =
        schema => new LocalReferenceResolver(new JsonSchemaAppender(schema, new DefaultTypeNameGenerator()));

    public override Task<IJsonReference> ResolveFileReferenceAsync(string filePath, CancellationToken cancellationToken = default)
    {
        throw new ExternalSchemaReferenceException();
    }

    public override Task<IJsonReference> ResolveUrlReferenceAsync(string url, CancellationToken cancellationToken = default)
    {
        throw new ExternalSchemaReferenceException();
    }
}
