using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using Newtonsoft.Json.Linq;

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

    /// <summary>
    /// Lets the user edit a JSON document in the preferred editor (set up on first use), validates it, shows the changes and asks for a confirmation.
    /// </summary>
    /// <param name="console">The console used for prompts and output.</param>
    /// <param name="options">The configured editor options.</param>
    /// <param name="original">The document to edit.</param>
    /// <param name="fileNamePrefix">A prefix of the temporary file name.</param>
    /// <param name="isNew">Whether the document doesn't exist on the server yet.</param>
    /// <param name="confirm">Whether to ask before accepting the edited document. The default is <c>true</c>.</param>
    /// <returns>The edited document, or the exit code when there is nothing to save.</returns>
    Task<JsonEditResult> EditJsonAsync(IExtendedConsole console, EditorOptions options, JObject original, string fileNamePrefix, bool isNew, bool confirm = true);
}
