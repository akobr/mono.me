using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Commands.Templates;

[Command(CommandNames.EDIT, Description = "Edit a template in your preferred editor.")]
public class TemplateEditCommand : BaseContextCommand
{
    private readonly ITemplatesApiClient _templatesApi;
    private readonly IEditorService _editorService;
    private readonly EditorOptions _editorOptions;

    public TemplateEditCommand(
        IExtendedConsole console,
        ICommandContext context,
        ITemplatesApiClient templatesApi,
        IEditorService editorService,
        IOptions<EditorOptions> editorOptions)
        : base(console, context)
    {
        _templatesApi = templatesApi;
        _editorService = editorService;
        _editorOptions = editorOptions.Value;
    }

    [Argument(0, Description = "An annotation type code (rst, unt, sbt, usg, cnt, exe, uxe) to edit the template for.")]
    public string AnnotationType { get; set; } = string.Empty;

    /// <summary>
    /// Opens the template in the configured editor and replaces the template with the edited document when the user confirms.
    /// </summary>
    /// <returns>
    /// An exit code indicating the outcome:
    /// - <c>ExitCodes.SUCCESS</c> when the edited template was saved;
    /// - <c>ExitCodes.WARNING_NO_WORK_NEEDED</c> when no changes were made;
    /// - <c>ExitCodes.WARNING_ABORTED</c> when the user aborted the operation;
    /// - <c>ExitCodes.ERROR_CRASH</c> when the editor exited with a non-zero code or the API rejected the change.
    /// </returns>
    protected override async Task<int> ExecuteAsync()
    {
        var annotationType = Console.ValidateAnnotationType(AnnotationType);
        var currentContent = await _templatesApi.GetTemplateContentAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Context.ViewName,
            annotationType);

        if (currentContent is null)
        {
            Console.WriteLine($"Template for '{annotationType}' does not exist yet, creating new.");
        }

        var result = await _editorService.EditJsonAsync(
            Console,
            _editorOptions,
            currentContent ?? new JObject(),
            $"template-{Context.ViewName}-{annotationType}",
            currentContent is null);

        if (result.Edited is null)
        {
            return result.ExitCode;
        }

        try
        {
            // replace (not merge), so removed properties and array items are removed on the server as well
            var saved = await _templatesApi.ReplaceTemplateAsync(
                Context.OrganizationName,
                Context.ProjectName,
                Context.ViewName,
                annotationType,
                currentContent,
                result.Edited);

            if (saved is null)
            {
                Console.WriteLine("No changes detected.");
                return ExitCodes.WARNING_NO_WORK_NEEDED;
            }

            Console.WriteImportant($"Template for '{annotationType}' has been saved (version {saved.Version}).");
            return ExitCodes.SUCCESS;
        }
        catch (ApiException e)
        {
            Console.WriteLine($"Error occurred: {e.Message}");
            return ExitCodes.ERROR_CRASH;
        }
    }
}
