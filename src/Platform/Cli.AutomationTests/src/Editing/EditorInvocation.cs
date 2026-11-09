using System.Text.Json.Nodes;

namespace _42.Platform.Cli.AutomationTests.Editing;

// One time sform opened the fake editor: the file it gave (a temporary file of sform, deleted after the run, so its
// content is kept here), the step done, the exit code, the text before and after, and the problem when there was one.
public sealed record EditorInvocation
{
    public int Invocation { get; init; }

    public string? Run { get; init; }

    public string Time { get; init; } = string.Empty;

    public string? File { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public string? Step { get; init; }

    public string? ChangeFile { get; init; }

    public int ExitCode { get; init; }

    // The document sform wrote into the file before it opened the editor.
    public string? Original { get; init; }

    // What the editor wrote into the file; null when it left the file as it was.
    public string? Written { get; init; }

    public string? Error { get; init; }

    // The file's name without the folder, e.g. "config-rst.cli-…-a1b2c3d4.json" (sform names it <prefix>-<random>.json).
    public string? FileName => File?[(File.LastIndexOfAny(['/', '\\']) + 1)..];

    public JsonNode? OriginalJson() => Original is null ? null : JsonNode.Parse(Original);

    public JsonNode? WrittenJson() => Written is null ? null : JsonNode.Parse(Written);

    public override string ToString()
    {
        return $"#{Invocation} {Run} {Step ?? "(no step)"} exit {ExitCode}: {FileName ?? "(no file)"}{(Error is null ? string.Empty : $" — {Error}")}";
    }
}
