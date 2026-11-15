using System.Globalization;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using SdkSettings = _42.Platform.Storyteller.Sdk.ProjectSettings;
using Settings = _42.Platform.Storyteller.ProjectSettings;

namespace _42.Platform.Cli.Commands.Projects;

// What the commands of sform project settings share: the read with the version, the changes and the outcome of a write.
internal static class ProjectSettingsCommands
{
    // The project of the context as <organization>.<project>.
    public static string ProjectKeyOf(ICommandContext context)
    {
        return $"{context.OrganizationName}.{context.ProjectName}";
    }

    // The settings of the context's project with the ETag of their version (built from the version when the answer has
    // no header). The settings always exist: a project that never set them has Version 0.
    public static async Task<(SdkSettings Settings, string ETag)> ReadAsync(IAccessApiClient accessApi, ICommandContext context)
    {
        var response = await accessApi.GetProjectSettingsConditionalAsync(context.OrganizationName, context.ProjectName);
        var settings = response.Result ?? new SdkSettings();
        return (settings, response.ETag ?? ProjectSettingsDocuments.ETagOf(settings.Version));
    }

    public static void WriteChanges(IExtendedConsole console, Settings before, Settings after)
    {
        console.WriteHeader("Changes");
        console.WriteDiff(ProjectSettingsDocuments.ToText(before), ProjectSettingsDocuments.ToText(after));
    }

    // After a write: the settings did not change (the API answers the version it had), or the changes and the new version.
    public static int WriteOutcome(IExtendedConsole console, SdkSettings before, SdkSettings after, bool showChanges)
    {
        if (after.Version == before.Version)
        {
            WriteUnchanged(console, after.Version);
            return ExitCodes.WARNING_NO_WORK_NEEDED;
        }

        if (showChanges)
        {
            WriteChanges(console, ProjectSettingsDocuments.ToModel(before), ProjectSettingsDocuments.ToModel(after));
        }

        console.WriteLine($"Saved the project settings, version {after.Version.ToString(CultureInfo.InvariantCulture)}.");
        return ExitCodes.SUCCESS;
    }

    public static void WriteUnchanged(IExtendedConsole console, long version)
    {
        console.WriteLine($"The project settings did not change; they stay at version {version.ToString(CultureInfo.InvariantCulture)}.");
    }
}
