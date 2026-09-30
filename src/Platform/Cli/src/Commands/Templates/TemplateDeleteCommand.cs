using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Templates;

[Command(CommandNames.DELETE, CommandNames.REMOVE, Description = "Delete a template (its history is kept).")]
public class TemplateDeleteCommand : BaseContextCommand
{
    private readonly ITemplatesApiClient _templatesApi;

    public TemplateDeleteCommand(
        IExtendedConsole console,
        ICommandContext context,
        ITemplatesApiClient templatesApi)
        : base(console, context)
    {
        _templatesApi = templatesApi;
    }

    [Argument(0, Description = "An annotation type code (rst, unt, sbt, usg, cnt, exe, uxe) to delete the template for.")]
    public string AnnotationType { get; set; } = string.Empty;

    protected override async Task<int> ExecuteAsync()
    {
        var annotationType = Console.ValidateAnnotationType(AnnotationType);

        try
        {
            await _templatesApi.DeleteTemplateAsync(
                Context.OrganizationName,
                Context.ProjectName,
                Context.ViewName,
                annotationType);
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound)
        {
            Console.WriteLine($"The template for '{annotationType}' has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        Console.WriteImportant($"The template for '{annotationType}' has been deleted.");
        return ExitCodes.SUCCESS;
    }
}
