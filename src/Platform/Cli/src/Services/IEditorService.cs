using System;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Services;

public interface IEditorService
{
    /// <summary>
    /// Asks for the editor and saves the choice in the settings file; a choice that cannot be saved is reported and returned.
    /// </summary>
    /// <param name="console">The extended console used to query or display editor preference prompts.</param>
    /// <returns>An <see cref="EditorOptions"/> instance representing the configured editor settings.</returns>
    EditorOptions SetupEditorPreference(IExtendedConsole console);

    /// <summary>
    /// Asks for the editor (Visual Studio Code, Neovim and Vim when they are installed, or a custom command) without saving it.
    /// </summary>
    /// <param name="console">The extended console used to query or display editor preference prompts.</param>
    /// <returns>The chosen editor; not configured when a custom command was chosen without entering one.</returns>
    EditorOptions PromptEditorPreference(IExtendedConsole console);

    /// <summary>
    /// Whether the executable of an editor is found on the PATH, or as a file for a custom command with a full path.
    /// </summary>
    /// <param name="options">The editor to look for.</param>
    /// <returns><c>true</c> when the executable is found.</returns>
    bool IsEditorAvailable(EditorOptions options);

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
    /// <param name="check">
    /// Checks the parsed document before the comparison and the question. A problem re-opens the editor, as invalid JSON
    /// does; the document of a valid check (for example normalized) replaces the parsed one for the comparison with
    /// <paramref name="original"/>, the diff, the question and the result. The default takes the parsed document.
    /// </param>
    /// <returns>The edited document, or the exit code when there is nothing to save.</returns>
    Task<JsonEditResult> EditJsonAsync(IExtendedConsole console, EditorOptions options, JObject original, string fileNamePrefix, bool isNew, bool confirm = true, Func<JObject, JsonEditCheck>? check = null);
}
