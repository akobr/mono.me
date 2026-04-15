using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;

namespace _42.Platform.Cli.Services;

public interface IEditorService
{
    EditorOptions SetupEditorPreference(IExtendedConsole console);

    Task<int> OpenFileInEditorAsync(string filePath, EditorOptions options);
}
