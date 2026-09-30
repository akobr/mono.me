using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Templates;

[Command(CommandNames.DIFF, Description = "Show difference between two template versions.")]
public class TemplateDiffCommand : BaseContextCommand
{
    private readonly ITemplatesApiClient _templatesApi;

    public TemplateDiffCommand(
        IExtendedConsole console,
        ICommandContext context,
        ITemplatesApiClient templatesApi)
        : base(console, context)
    {
        _templatesApi = templatesApi;
    }

    [Argument(0, Description = "An annotation type code (rst, unt, sbt, usg, cnt, exe, uxe) to get the template diff for.")]
    public string AnnotationType { get; set; } = string.Empty;

    [Argument(1, Description = "The version to compare (to). If not specified, the latest version is used.")]
    public string? ToVersion { get; set; }

    [Argument(2, Description = "The version to compare from. If not specified, the previous version of 'to' is used.")]
    public string? FromVersion { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var annotationType = Console.ValidateAnnotationType(AnnotationType);

        if (!string.IsNullOrWhiteSpace(ToVersion) && !int.TryParse(ToVersion, out _))
        {
            Console.WriteLine($"ToVersion '{ToVersion}' is not a valid integer.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        if (!string.IsNullOrWhiteSpace(FromVersion) && !int.TryParse(FromVersion, out _))
        {
            Console.WriteLine($"FromVersion '{FromVersion}' is not a valid integer.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        try
        {
            DiffResult diff;

            if (int.TryParse(ToVersion, out var toVersion))
            {
                diff = int.TryParse(FromVersion, out var fromVersion)
                    ? await _templatesApi.GetTemplateVersionDiffCustomAsync(
                        Context.OrganizationName,
                        Context.ProjectName,
                        Context.ViewName,
                        annotationType,
                        toVersion,
                        fromVersion,
                        null)
                    : await _templatesApi.GetTemplateVersionDiffAsync(
                        Context.OrganizationName,
                        Context.ProjectName,
                        Context.ViewName,
                        annotationType,
                        toVersion,
                        null);
            }
            else
            {
                // Default: compare latest version with its previous
                var versions = await _templatesApi.GetTemplateVersionsAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    Context.ViewName,
                    annotationType);

                if (versions.Count == 0)
                {
                    Console.WriteLine($"The template for '{annotationType}' has no versions.");
                    return ExitCodes.ERROR_WRONG_INPUT;
                }

                var latestVersion = versions.Max(v => v.Version);
                diff = await _templatesApi.GetTemplateVersionDiffAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    Context.ViewName,
                    annotationType,
                    latestVersion,
                    null);
            }

            Console.WriteDiffResult(diff);
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound)
        {
            Console.WriteLine($"The template for '{annotationType}' or the requested version has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        return ExitCodes.SUCCESS;
    }
}
