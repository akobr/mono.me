using System.Collections.Generic;
using System.Net;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services.Projects;
using _42.Platform.Storyteller;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Sharprompt;
using Appearance = _42.Platform.Storyteller.ProjectTypeAppearance;
using SdkSettings = _42.Platform.Storyteller.Sdk.ProjectSettings;
using Settings = _42.Platform.Storyteller.ProjectSettings;

namespace _42.Platform.Cli.Commands.Projects;

[Command(CommandNames.RESET, Description = "Reset the settings of the project to the defaults: all of them, or only the parts named. Needs the Administrator role.")]
public class ProjectSettingsResetCommand : BaseContextCommand
{
    private readonly IAccessApiClient _accessApi;
    private readonly IProjectSettingsCache _cache;

    public ProjectSettingsResetCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAccessApiClient accessApi,
        IProjectSettingsCache cache)
        : base(console, context)
    {
        _accessApi = accessApi;
        _cache = cache;
    }

    [Option("--types", CommandOptionType.NoValue, Description = "Reset the names, colors and icons of the annotation types to the built-in ones.")]
    public bool IsTypesReset { get; set; }

    [Option("--time-zone", CommandOptionType.NoValue, Description = "Reset the default time zone to UTC.")]
    public bool IsTimeZoneReset { get; set; }

    [Option("--date-format", CommandOptionType.NoValue, Description = "Reset the date format to yyyy-mm-dd.")]
    public bool IsDateFormatReset { get; set; }

    [Option("-y|--yes", CommandOptionType.NoValue, Description = "Reset without asking for a confirmation.")]
    public bool Yes { get; set; }

    /// <summary>
    /// Removes the named parts of the settings (all of them without a part) by a merge patch, after a confirmation, only
    /// while they are at the version that was read and shown.
    /// </summary>
    /// <returns>
    /// An exit code indicating the outcome:
    /// - <c>ExitCodes.SUCCESS</c> when the settings were reset;
    /// - <c>ExitCodes.WARNING_NO_WORK_NEEDED</c> when they are at the defaults already;
    /// - <c>ExitCodes.WARNING_ABORTED</c> when the user declined;
    /// - <c>ExitCodes.WARNING_INTERACTION_NEEDED</c> when the input is redirected and -y|--yes is missing;
    /// - <c>ExitCodes.ERROR_WRONG_INPUT</c> when they changed meanwhile (412);
    /// - <c>ExitCodes.WARNING_UNAUTHORIZED_ACCESS</c> when the API refused the change (403).
    /// </returns>
    protected override async Task<int> ExecuteAsync()
    {
        var isAll = !IsTypesReset && !IsTimeZoneReset && !IsDateFormatReset;
        var projectKey = ProjectSettingsCommands.ProjectKeyOf(Context);
        SdkSettings before;
        string etag;

        try
        {
            (before, etag) = await ProjectSettingsCommands.ReadAsync(_accessApi, Context);
        }
        catch (ApiException e) when (e.StatusCode != (int)HttpStatusCode.Unauthorized)
        {
            return Console.WriteApiError(e);
        }

        var current = ProjectSettingsDocuments.ToModel(before);
        var reset = current with
        {
            Types = isAll || IsTypesReset ? new Dictionary<string, Appearance>() : current.Types,
            DefaultTimeZone = isAll || IsTimeZoneReset ? null : current.DefaultTimeZone,
            DateFormat = isAll || IsDateFormatReset ? null : current.DateFormat,
        };

        if (ProjectSettingsRules.SameValues(current, reset))
        {
            ProjectSettingsCommands.WriteUnchanged(Console, before.Version);
            return ExitCodes.WARNING_NO_WORK_NEEDED;
        }

        if (!Yes)
        {
            // After the read, so a project that cannot be read and settings at the defaults end with their own codes.
            Console.EnsureInteractive("the confirmation of the reset", "Pass -y|--yes to reset them without asking.");
            ProjectSettingsCommands.WriteChanges(Console, current, reset);

            var isConfirmed = Console.Confirm(new ConfirmOptions
            {
                Message = $"Reset {Describe(isAll)} of {projectKey} to the defaults",
                DefaultValue = false,
            });

            if (!isConfirmed)
            {
                Console.WriteLine("Nothing was reset.");
                return ExitCodes.WARNING_ABORTED;
            }
        }

        VersionedResponse<SdkSettings> response;

        try
        {
            // Only while the settings are what was read (and shown).
            response = await _accessApi.PatchProjectSettingsVersionedAsync(
                Context.OrganizationName,
                Context.ProjectName,
                ProjectSettingsDocuments.ToNewtonsoft(Patch(isAll)),
                etag);
        }
        catch (ApiException e) when (e.StatusCode != (int)HttpStatusCode.Unauthorized)
        {
            return Console.WriteSettingsFailure(e);
        }

        var after = response.Result;
        await _cache.RememberAsync(Context.OrganizationName, Context.ProjectName, after, response.ETag ?? ProjectSettingsDocuments.ETagOf(after.Version));

        // Without the question the changes were not shown yet.
        return ProjectSettingsCommands.WriteOutcome(Console, before, after, showChanges: Yes);
    }

    // The merge patch that removes the parts: null is "none", the defaults.
    private JsonObject Patch(bool isAll)
    {
        var patch = new JsonObject();

        if (isAll || IsTypesReset)
        {
            patch[nameof(Settings.Types)] = null;
        }

        if (isAll || IsTimeZoneReset)
        {
            patch[nameof(Settings.DefaultTimeZone)] = null;
        }

        if (isAll || IsDateFormatReset)
        {
            patch[nameof(Settings.DateFormat)] = null;
        }

        return patch;
    }

    private string Describe(bool isAll)
    {
        if (isAll)
        {
            return "the types, the default time zone and the date format";
        }

        var parts = new List<string>();

        if (IsTypesReset)
        {
            parts.Add("the types");
        }

        if (IsTimeZoneReset)
        {
            parts.Add("the default time zone");
        }

        if (IsDateFormatReset)
        {
            parts.Add("the date format");
        }

        return parts.Count switch
        {
            1 => parts[0],
            2 => $"{parts[0]} and {parts[1]}",
            _ => $"{string.Join(", ", parts.GetRange(0, parts.Count - 1))} and {parts[^1]}",
        };
    }
}
