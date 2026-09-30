using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Commands.Configuration;

[Command(CommandNames.EDIT, Description = "Edit the stored content of a configuration in your preferred editor.")]
public class ConfigEditCommand : BaseContextCommand
{
    private readonly IConfigurationsApiClient _configurationApi;
    private readonly IEditorService _editorService;
    private readonly EditorOptions _editorOptions;

    /// <summary>
    /// Initializes a new instance of <see cref="ConfigEditCommand"/> with the specified services and options.
    /// </summary>
    /// <param name="editorOptions">Provides editor-related configuration used by the command.</param>
    public ConfigEditCommand(
        IExtendedConsole console,
        ICommandContext context,
        IConfigurationsApiClient configurationApi,
        IEditorService editorService,
        IOptions<EditorOptions> editorOptions)
        : base(console, context)
    {
        _configurationApi = configurationApi;
        _editorService = editorService;
        _editorOptions = editorOptions.Value;
    }

    [Argument(0, Description = "An annotation key to edit the configuration for.")]
    public string AnnotationKey { get; set; } = string.Empty;

    /// <summary>
    /// Opens the stored content of the configuration (without inherited values and templates) in the configured editor,
    /// and replaces the stored content with the edited document when the user confirms.
    /// </summary>
    /// <returns>
    /// An exit code indicating the outcome:
    /// - <c>ExitCodes.SUCCESS</c> when the edited configuration was saved;
    /// - <c>ExitCodes.WARNING_NO_WORK_NEEDED</c> when no changes were made;
    /// - <c>ExitCodes.WARNING_ABORTED</c> when the user aborted the operation;
    /// - <c>ExitCodes.ERROR_CRASH</c> when the editor exited with a non-zero code or the API rejected the change.
    /// </returns>
    protected override async Task<int> ExecuteAsync()
    {
        var storedContent = await _configurationApi.GetStoredConfigurationContentAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Context.ViewName,
            AnnotationKey);

        if (storedContent is null)
        {
            Console.WriteLine($"Configuration for '{AnnotationKey}' does not exist yet, creating new.");
        }

        var result = await _editorService.EditJsonAsync(
            Console,
            _editorOptions,
            storedContent ?? new JObject(),
            $"config-{AnnotationKey}",
            storedContent is null);

        if (result.Edited is null)
        {
            return result.ExitCode;
        }

        try
        {
            // replace (not merge), so removed properties and array items are removed on the server as well
            var saved = await _configurationApi.ReplaceConfigurationAsync(
                Context.OrganizationName,
                Context.ProjectName,
                Context.ViewName,
                AnnotationKey,
                storedContent,
                result.Edited);

            if (saved is null)
            {
                Console.WriteLine("No changes detected.");
                return ExitCodes.WARNING_NO_WORK_NEEDED;
            }

            Console.WriteImportant($"Configuration for '{AnnotationKey}' has been saved (version {saved.Version}).");
            return ExitCodes.SUCCESS;
        }
        catch (ApiException e)
        {
            Console.WriteLine($"Error occurred: {e.Message}");
            return ExitCodes.ERROR_CRASH;
        }
    }
}
