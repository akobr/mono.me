using System.Text.RegularExpressions;

namespace _42.Platform.Storyteller.Mcp.Tools;

// The name rule of sform (AssistKeyCommand) for new annotations: the API only forbids dots, but names with spaces or
// other characters are awkward in keys, paths and bindings. Existing keys are addressed as they are.
public static partial class AnnotationNames
{
    public const int MaxLength = 50;

    public static bool IsValid(string? name)
    {
        return !string.IsNullOrWhiteSpace(name) && name.Length <= MaxLength && NamePattern().IsMatch(name);
    }

    public static string Ensure(string? name, string parameter)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ToolInputException($"'{parameter}' can't be empty.");
        }

        if (name.Length > MaxLength)
        {
            throw new ToolInputException($"'{parameter}' is too long, max {MaxLength} characters.");
        }

        if (!NamePattern().IsMatch(name))
        {
            throw new ToolInputException($"'{parameter}' can contain only letters, numbers, dash and underscore.");
        }

        return name;
    }

    [GeneratedRegex(@"^[a-zA-Z0-9\-_]+$")]
    private static partial Regex NamePattern();
}
