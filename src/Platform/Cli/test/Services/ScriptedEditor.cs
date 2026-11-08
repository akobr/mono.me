using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Threading.Tasks;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Services;

namespace _42.Platform.Cli.UnitTests.Services;

// The real EditorService with a stand-in for the editor: each open records the text and the path of the file, then
// writes the next scripted text into it (null leaves the file as it is).
internal sealed class ScriptedEditor(MockFileSystem fileSystem, IEnumerable<string?> edits) : EditorService(fileSystem)
{
    public static readonly EditorOptions Vim = new() { EditorType = EditorType.Vim };

    private readonly Queue<string?> _edits = new(edits);

    public List<string> Opened { get; } = [];

    public List<string> Paths { get; } = [];

    public int ExitCode { get; init; }

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
}
