using _42.Platform.Cli.Commands.Projects;
using _42.Platform.Cli.Services.Projects;

namespace _42.Platform.Cli.Output;

// The name of an annotation type as the API writes it in an answer (a code such as unt, or a name such as Unit or
// UnitOfExecution) in the console output: the project's name of the type, else the text as it is.
internal static class TypeNames
{
    public static string Of(string? text, ProjectDisplay display)
    {
        var value = text ?? string.Empty;
        return display.HasTypes && ProjectSettingsDocuments.TryGetTypeCode(value, out var code)
            ? display.NameOr(code, value)
            : value;
    }
}
