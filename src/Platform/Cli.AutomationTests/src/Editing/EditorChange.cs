using System.Globalization;
using System.IO.Abstractions;
using System.Text;
using System.Text.Json.Nodes;

namespace _42.Platform.Cli.AutomationTests.Editing;

// What the fake editor does when sform opens it (mode E): the steps of CLI_EDITOR_MODE and the files they read. A test
// passes it to FakeEditor.With, which writes the files (named after the run, in the editor's own folder) and returns the
// run's options:
//   var editor = await UseEditorAsync();
//   (await Sform(editor.With(EditorChange.Merge("""{ "a": 2 }""")), InProject("story", "config", "edit", key, "-y"))).ShouldSucceed();
// sform's reactions (EditorService.EditJsonAsync): a changed document is shown as a diff and saved (with -y, else after a
// confirmation in a terminal); None is "No changes detected." and 42; Invalid is "Invalid JSON: …" and "Nothing was
// saved." with 2 on redirected input, and the offer to open the editor again in a terminal; Exit(n) with n other than 0 is
// "Editor exited with code n." and 1.
public sealed class EditorChange
{
    private EditorChange(IReadOnlyList<Step> steps)
    {
        Steps = steps;
    }

    // The file is left as it is: the "no change" path.
    public static EditorChange None { get; } = new([new Step("none", null)]);

    // The file becomes text that is not JSON.
    public static EditorChange Invalid { get; } = new([new Step("invalid", null)]);

    // The steps of the invocations; the last one repeats when sform opens the editor more often.
    internal IReadOnlyList<Step> Steps { get; }

    // The file becomes this content (not checked: Replace("not json") is another invalid document).
    public static EditorChange Replace(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new([new Step("replace", content)]);
    }

    // An RFC 7396 JSON merge patch applied to the document sform wrote: members merge, null removes one, anything that is
    // not an object replaces the whole document.
    public static EditorChange Merge(string mergePatch)
    {
        ArgumentNullException.ThrowIfNull(mergePatch);
        _ = JsonNode.Parse(mergePatch);
        return new([new Step("merge", mergePatch)]);
    }

    public static EditorChange Merge(JsonNode mergePatch)
    {
        ArgumentNullException.ThrowIfNull(mergePatch);
        return new([new Step("merge", mergePatch.ToJsonString())]);
    }

    // The editor exits with the code and leaves the file as it is.
    public static EditorChange Exit(int code)
    {
        return new([new Step($"exit:{code.ToString(CultureInfo.InvariantCulture)}", null)]);
    }

    // One change per invocation of the run, e.g. Sequence(Invalid, Merge(…)) for a terminal case that opens the editor again
    // after the invalid document; the last step repeats.
    public static EditorChange Sequence(params EditorChange[] changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Length == 0)
        {
            throw new ArgumentException("A sequence needs at least one change.", nameof(changes));
        }

        return new([.. changes.SelectMany(change => change.Steps)]);
    }

    // The variables of one run: each step's content in a file of the directory, CLI_EDITOR_MODE, CLI_EDITOR_FILE_<n>,
    // CLI_EDITOR_LOG and CLI_EDITOR_RUN.
    public IReadOnlyDictionary<string, string> ToEnvironment(string directory, string logPath, string run, IFileSystem fileSystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(run);
        ArgumentNullException.ThrowIfNull(fileSystem);

        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FakeEditor.ModeVariable] = string.Join(',', Steps.Select(step => step.Mode)),
            [FakeEditor.LogVariable] = logPath,
            [FakeEditor.RunVariable] = run,
        };

        for (var index = 0; index < Steps.Count; index++)
        {
            if (Steps[index].Content is not { } content)
            {
                continue;
            }

            fileSystem.Directory.CreateDirectory(directory);
            var file = fileSystem.Path.Combine(directory, $"{run}-step-{index + 1}.json");
            fileSystem.File.WriteAllText(file, content, new UTF8Encoding(false));
            environment[$"{FakeEditor.FileVariable}_{(index + 1).ToString(CultureInfo.InvariantCulture)}"] = file;
        }

        return environment;
    }

    public override string ToString()
    {
        return string.Join(", ", Steps.Select(step => step.Mode));
    }

    internal sealed record Step(string Mode, string? Content);
}
