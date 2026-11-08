using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Services;

/// <summary>
/// What a caller of <see cref="IEditorService.EditJsonAsync"/> makes of an edited document: the document to compare, show
/// and return in its place, or the problem that re-opens the editor.
/// </summary>
/// <param name="Document">The document that replaces the edited one; null when the document is not valid.</param>
/// <param name="Problem">What is wrong with the document; null when it is valid.</param>
public sealed record class JsonEditCheck(JObject? Document, string? Problem)
{
    public static JsonEditCheck Valid(JObject document) => new(document, null);

    public static JsonEditCheck Invalid(string problem) => new(null, problem);
}
