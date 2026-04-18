using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Spectre.Console;

namespace _42.Platform.Cli.Commands.Configuration;

[Command(CommandNames.DIFF, Description = "Show difference between two configuration versions.")]
public class ConfigDiffCommand : BaseContextCommand
{
    private readonly IConfigurationApiClient _configurationApi;

    /// <summary>
    /// Initializes a new instance of ConfigDiffCommand with the required console, command context, and configuration API client.
    /// </summary>
    public ConfigDiffCommand(
        IExtendedConsole console,
        ICommandContext context,
        IConfigurationApiClient configurationApi)
        : base(console, context)
    {
        _configurationApi = configurationApi;
    }

    [Argument(0, Description = "An annotation key to get the configuration diff for.")]
    public string AnnotationKey { get; set; } = string.Empty;

    [Argument(1, Description = "The version to compare (to). If not specified, the latest version is used.")]
    public string? ToVersion { get; set; }

    [Argument(2, Description = "The version to compare from. If not specified, the previous version of 'to' is used.")]
    public string? FromVersion { get; set; }

    [Option("--view-to-compare", CommandOptionType.SingleValue, Description = "The view to compare with. Current view is used as from and the specified view as to.")]
    public string? TargetView { get; set; }

    /// <summary>
    /// Runs the diff command: validates arguments, retrieves the requested configuration diff, and writes a formatted, colorized diff to the console.
    /// </summary>
    /// <remarks>
    /// Validation enforces that the view-to-compare option and version arguments are mutually exclusive and that any provided version values parse as integers.
    /// Diff selection precedence:
    /// - If a target view is specified, compare the current view to that view.
    /// - Else if a `ToVersion` is provided (and optionally `FromVersion`), request the corresponding version diff.
    /// - Otherwise, compare the latest version against its previous version.
    /// If no diff lines are returned, the method prints "No changes detected." If the configuration has no versions or the annotation is not found, an error message is written.
    /// </remarks>
    /// <returns>
    /// An exit code indicating the result:
    /// `ExitCodes.SUCCESS` when the diff is displayed or when no changes are detected; `ExitCodes.ERROR_WRONG_INPUT` for invalid arguments, missing versions, or when the annotation is not found.
    /// </returns>
    protected override async Task<int> ExecuteAsync()
    {
        try
        {
            // Validation: mutual exclusion check
            if (!string.IsNullOrWhiteSpace(TargetView) &&
                (!string.IsNullOrWhiteSpace(ToVersion) || !string.IsNullOrWhiteSpace(FromVersion)))
            {
                Console.WriteLine("Cannot specify both --view-to-compare and version arguments (ToVersion or FromVersion).");
                return ExitCodes.ERROR_WRONG_INPUT;
            }

            // Validation: integer parsing check
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

            ICollection<string> diffLines;

            if (!string.IsNullOrWhiteSpace(TargetView))
            {
                diffLines = await _configurationApi.GetConfigurationViewDiffAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    Context.ViewName,
                    AnnotationKey,
                    TargetView);
            }
            else if (int.TryParse(ToVersion, out var toVersion))
            {
                if (int.TryParse(FromVersion, out var fromVersion))
                {
                    diffLines = await _configurationApi.GetConfigurationVersionDiffCustomAsync(
                        Context.OrganizationName,
                        Context.ProjectName,
                        Context.ViewName,
                        AnnotationKey,
                        toVersion,
                        fromVersion);
                }
                else
                {
                    diffLines = await _configurationApi.GetConfigurationVersionDiffAsync(
                        Context.OrganizationName,
                        Context.ProjectName,
                        Context.ViewName,
                        AnnotationKey,
                        toVersion);
                }
            }
            else
            {
                // Default: compare latest version with its previous
                var versions = await _configurationApi.GetConfigurationVersionsAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    Context.ViewName,
                    AnnotationKey);

                if (versions.Count == 0)
                {
                    Console.WriteLine($"The configuration for '{AnnotationKey}' has no versions.");
                    return ExitCodes.ERROR_WRONG_INPUT;
                }

                var latestVersion = versions.OrderByDescending(v => v.Version).First().Version;
                diffLines = await _configurationApi.GetConfigurationVersionDiffAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    Context.ViewName,
                    AnnotationKey,
                    latestVersion);
            }

            if (diffLines == null || diffLines.Count == 0)
            {
                Console.WriteLine("No changes detected.");
                return ExitCodes.SUCCESS;
            }

            var numWidth = diffLines.Count.ToString().Length;
            var lineNumber = 1;

            Console.WriteLine();
            foreach (var line in diffLines)
            {
                var isRemoved = line.StartsWith('-');
                var linePrefix = isRemoved
                    ? new string(' ', numWidth)
                    : lineNumber.ToString().PadLeft(numWidth);

                AnsiConsole.Markup($"[grey]{linePrefix}[/] [dim]│[/] ");

                if (line.StartsWith('+'))
                {
                    AnsiConsole.MarkupLine($"[green]{Markup.Escape(line)}[/]");
                    lineNumber++;
                }
                else if (isRemoved)
                {
                    AnsiConsole.MarkupLine($"[red]{Markup.Escape(line)}[/]");
                }
                else if (line.StartsWith('^'))
                {
                    AnsiConsole.MarkupLine($"[blue]{Markup.Escape(line)}[/]");
                    lineNumber++;
                }
                else
                {
                    AnsiConsole.WriteLine(line);
                    lineNumber++;
                }
            }

            Console.WriteLine();
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound)
        {
            Console.WriteLine($"The configuration for '{AnnotationKey}' has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        return ExitCodes.SUCCESS;
    }
}