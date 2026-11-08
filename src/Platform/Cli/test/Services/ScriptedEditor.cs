using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Services;

namespace _42.Platform.Cli.UnitTests.Services;

// The real EditorService with a stand-in for the editor: each open records the text and the path of the file, then
// writes the next scripted text into it (null leaves the file as it is).
internal sealed class ScriptedEditor(MockFileSystem fileSystem, IEnumerable<string?> edits)
    : EditorService(fileSystem, new UserSettingsStore(fileSystem, MockUnixSupport.Path(@"C:\Users\ada\.42for.net\sform.settings.json"), new Dictionary<string, string>()), EnvironmentEditor.None)
{
    public static readonly EditorOptions Vim = new() { EditorType = EditorType.Vim };

    private readonly Queue<string?> _edits = new(edits);

    public List<string> Opened { get; } = [];

    public List<string> Paths { get; } = [];

    public int ExitCode { get; init; }

    // The documents of each diff the user was asked about: the original and the edited one, as the check gave it.
    public List<(string Original, string Edited)> Diffs { get; } = [];

    public override Task<int> OpenFileInEditorAsync(string filePath, EditorOptions options)
    {
        Paths.Add(filePath);
        Opened.Add(fileSystem.File.ReadAllText(filePath));

        if (_edits.TryDequeue(out var edit) && edit is not null)
        {
            fileSystem.File.WriteAllText(filePath, edit);
        }

        return Task.FromResult(ExitCode);
    }

    protected override void ShowChanges(IExtendedConsole console, bool isNew, string originalJson, string editedJson)
    {
        Diffs.Add((originalJson, editedJson));
    }
}
