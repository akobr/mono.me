using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;

namespace _42.Platform.Cli.Services;

public interface IEditorService
{
    /// <summary>
/// Configure editor preferences using the provided extended console and produce the resulting options.
/// </summary>
/// <param name="console">The extended console used to query or display editor preference prompts.</param>
/// <returns>An <see cref="EditorOptions"/> instance representing the configured editor settings.</returns>
EditorOptions SetupEditorPreference(IExtendedConsole console);

    /// <summary>
/// Opens the specified file using the provided editor options.
/// </summary>
/// <param name="filePath">Path to the file to open.</param>
/// <param name="options">Editor configuration and launch options to use when opening the file.</param>
/// <returns>An integer exit or status code from the editor operation; conventionally `0` indicates success.</returns>
Task<int> OpenFileInEditorAsync(string filePath, EditorOptions options);
}
