using System.Collections.Generic;
using System.Globalization;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services.Projects;
using _42.Platform.Storyteller;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using SdkAppearance = _42.Platform.Storyteller.Sdk.ProjectTypeAppearance;
using SdkSettings = _42.Platform.Storyteller.Sdk.ProjectSettings;
using Settings = _42.Platform.Storyteller.ProjectSettings;

namespace _42.Platform.Cli.Commands.Projects;

[Command(CommandNames.SET, Description = "Change the settings of the project: merged into them, or replacing them with --replace. Needs the Administrator role.")]
public class ProjectSettingsSetCommand : BaseContextCommand
{
    private const string TypesMember = nameof(Settings.Types);
    private const string DefaultTimeZoneMember = nameof(Settings.DefaultTimeZone);
    private const string DateFormatMember = nameof(Settings.DateFormat);
    private const string LabelMember = nameof(SdkAppearance.Label);
    private const string PluralMember = nameof(SdkAppearance.Plural);
    private const string ColorMember = nameof(SdkAppearance.Color);
    private const string IconMember = nameof(SdkAppearance.Icon);

    private static readonly JsonDocumentOptions FileOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        AllowDuplicateProperties = false,
    };

    private readonly IAccessApiClient _accessApi;
    private readonly IFileSystem _fileSystem;
    private readonly IProjectSettingsCache _cache;

    public ProjectSettingsSetCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAccessApiClient accessApi,
        IFileSystem fileSystem,
        IProjectSettingsCache cache)
        : base(console, context)
    {
        _accessApi = accessApi;
        _fileSystem = fileSystem;
        _cache = cache;
    }

    [Option("-i|--import", CommandOptionType.SingleValue, ValueName = "file", Description = "A settings file (of 'sform project settings -e' or '-f json', or a data set's project-settings.json), applied first: merged into the settings, or all of them with --replace.")]
    public string? ImportFilePath { get; set; }

    [Option("--replace", CommandOptionType.NoValue, Description = "Replace the settings with the result instead of merging into them: a type left out goes back to its built-in appearance, a time zone or date format left out to its default.")]
    public bool IsReplaceRequested { get; set; }

    [Option("--type", CommandOptionType.SingleValue, ValueName = "type", Description = "The annotation type that --label, --plural, --color, --icon and --clear change: its code (rst, unt, sbt, usg, cnt, exe, uxe) or its built-in name (subject, unit of execution), in any case; one type per run.")]
    public string? TypeName { get; set; }

    [Option("--label", CommandOptionType.SingleValue, ValueName = "text", Description = "The project's name of the type, e.g. Company (at most 40 characters); an empty value removes it. Needs --type.")]
    public string? Label { get; set; }

    [Option("--plural", CommandOptionType.SingleValue, ValueName = "text", Description = "The plural of that name, e.g. Companies; only with a name, derived from it when missing; an empty value removes it. Needs --type.")]
    public string? Plural { get; set; }

    [Option("--color", CommandOptionType.SingleValue, ValueName = "name", Description = "The color of the type: red, orange, amber, yellow, lime, green, emerald, teal, cyan, sky, blue, indigo, violet, purple, fuchsia, pink, rose or gray; an empty value goes back to the built-in one. Needs --type.")]
    public string? Color { get; set; }

    [Option("--icon", CommandOptionType.SingleValue, ValueName = "name", Description = "The icon of the type, a Lucide icon of the catalog such as building-2 or i-lucide-building-2; an empty value goes back to the built-in one. Needs --type.")]
    public string? Icon { get; set; }

    [Option("--clear", CommandOptionType.NoValue, Description = "Remove the project's appearance of the --type (back to the built-in one), before --label, --plural, --color and --icon apply.")]
    public bool IsClearRequested { get; set; }

    [Option("--time-zone", CommandOptionType.SingleValue, ValueName = "iana-id", Description = "The time zone new annotations start in, an IANA id such as Europe/Prague or UTC; an empty value goes back to UTC.")]
    public string? TimeZone { get; set; }

    [Option("--date-format", CommandOptionType.SingleValue, ValueName = "format", Description = "How dates are shown: yyyy-mm-dd, dd.mm.yyyy, dd/mm/yyyy, mm/dd/yyyy or d mmm yyyy; an empty value goes back to yyyy-mm-dd.")]
    public string? DateFormat { get; set; }

    [Option("--expected-version", CommandOptionType.SingleValue, ValueName = "version", Description = "Change the settings only while they are at this version (sent as If-Match); otherwise nothing is written.")]
    public long? ExpectedVersion { get; set; }

    [Option("--dry-run", CommandOptionType.NoValue, Description = "Check the change against the current settings and show it, without saving it.")]
    public bool IsDryRun { get; set; }

    private bool HasTypeChange => Label is not null || Plural is not null || Color is not null || Icon is not null || IsClearRequested;

    /// <summary>
    /// Builds a merge patch of the -i file and the options, and sends it as a PATCH, or the resulting settings as a PUT
    /// with --replace; the API checks the rules and the role.
    /// </summary>
    /// <returns>
    /// An exit code indicating the outcome:
    /// - <c>ExitCodes.SUCCESS</c> when the settings were saved (or, with --dry-run, would change);
    /// - <c>ExitCodes.WARNING_NO_WORK_NEEDED</c> when they did not (or would not) change;
    /// - <c>ExitCodes.ERROR_WRONG_INPUT</c> when no option changes anything, the options or the file are wrong, the
    ///   settings break a rule (400), or they are not at the --expected-version (412);
    /// - <c>ExitCodes.WARNING_UNAUTHORIZED_ACCESS</c> when the API refused the change (403: not an Administrator, a machine).
    /// </returns>
    protected override async Task<int> ExecuteAsync()
    {
        var patch = await BuildPatchAsync();

        if (patch is null)
        {
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        SdkSettings before;

        try
        {
            (before, _) = await ProjectSettingsCommands.ReadAsync(_accessApi, Context);
        }
        catch (ApiException e) when (e.StatusCode != (int)HttpStatusCode.Unauthorized)
        {
            return Console.WriteApiError(e);
        }

        if (ExpectedVersion is { } expected && expected != before.Version)
        {
            Console.WriteChangedMeanwhile(before.Version);
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        if (IsDryRun)
        {
            return WriteDryRun(before, patch);
        }

        VersionedResponse<SdkSettings> response;
        var ifMatch = ExpectedVersion is { } version ? ProjectSettingsDocuments.ETagOf(version) : null;

        try
        {
            response = IsReplaceRequested
                ? await _accessApi.SetProjectSettingsVersionedAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    ToBody(ProjectSettingsJson.ApplyMergePatch(new JsonObject(), patch)),
                    ifMatch)
                : await _accessApi.PatchProjectSettingsVersionedAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    ProjectSettingsDocuments.ToNewtonsoft(patch),
                    ifMatch);
        }
        catch (ApiException e) when (e.StatusCode != (int)HttpStatusCode.Unauthorized)
        {
            return Console.WriteSettingsFailure(e);
        }

        var after = response.Result;
        await _cache.RememberAsync(Context.OrganizationName, Context.ProjectName, after, response.ETag ?? ProjectSettingsDocuments.ETagOf(after.Version));
        return ProjectSettingsCommands.WriteOutcome(Console, before, after, showChanges: true);
    }

    // The change applied to the current settings here, by the rules the API applies, and shown; nothing is written.
    private int WriteDryRun(SdkSettings before, JsonObject patch)
    {
        var current = ProjectSettingsDocuments.ToModel(before);
        var check = IsReplaceRequested
            ? ProjectSettingsJson.Read(ProjectSettingsJson.ApplyMergePatch(new JsonObject(), patch))
            : ProjectSettingsJson.ReadMergePatch(current, patch);

        if (!check.IsValid)
        {
            Console.WriteSettingsProblems(check.Problems);
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        var version = before.Version.ToString(CultureInfo.InvariantCulture);

        if (ProjectSettingsRules.SameValues(current, check.Settings))
        {
            Console.WriteLine($"The project settings would not change; they stay at version {version}.");
            return ExitCodes.WARNING_NO_WORK_NEEDED;
        }

        ProjectSettingsCommands.WriteChanges(Console, current, check.Settings);
        Console.WriteLine($"Dry run: nothing was saved; the project settings stay at version {version}.");
        return ExitCodes.SUCCESS;
    }

    // The merge patch of the -i file and then the options, in the members' canonical names; null after a message when
    // the command line is wrong.
    private async Task<JsonObject?> BuildPatchAsync()
    {
        string? code = null;

        if (TypeName is null && HasTypeChange)
        {
            Console.WriteImportant("--label, --plural, --color, --icon and --clear need --type, the type they change.");
            return null;
        }

        if (TypeName is not null)
        {
            if (!ProjectSettingsDocuments.TryGetTypeCode(TypeName, out var typeCode))
            {
                Console.WriteImportant($"'{TypeName}' is no annotation type; give a type code or its built-in name: {ProjectSettingsDocuments.DescribeTypes()}.");
                return null;
            }

            if (!HasTypeChange)
            {
                Console.WriteImportant($"--type {TypeName} changes nothing on its own; add --label, --plural, --color, --icon or --clear.");
                return null;
            }

            code = typeCode;
        }

        if (ImportFilePath is null && code is null && TimeZone is null && DateFormat is null)
        {
            Console.WriteImportant("Nothing to change: give -i|--import, --type with --label, --plural, --color, --icon or --clear, --time-zone, or --date-format.");
            return null;
        }

        if (ExpectedVersion is < 0)
        {
            Console.WriteImportant("--expected-version takes the version of the settings, 0 or more.");
            return null;
        }

        var patch = new JsonObject();

        if (ImportFilePath is not null)
        {
            if (await ReadImportAsync(ImportFilePath) is not { } imported)
            {
                return null;
            }

            patch = ProjectSettingsJson.ToJson(imported, ProjectSettingsJsonShape.Api);
            patch.Remove(nameof(Settings.Version));
            patch.Remove(nameof(Settings.Modified));
        }

        if (code is not null)
        {
            if (patch[TypesMember] is not JsonObject types)
            {
                types = new JsonObject();
                patch[TypesMember] = types;
            }

            types[code] = TypePatch(types[code] as JsonObject);
        }

        if (TimeZone is not null)
        {
            patch[DefaultTimeZoneMember] = ValueOf(TimeZone.Trim());
        }

        if (DateFormat is not null)
        {
            patch[DateFormatMember] = ValueOf(DateFormat.Trim());
        }

        return patch;
    }

    // The patch of the --type: the -i file's appearance of it (none with --clear, which removes every member first), then
    // the options; null removes the whole entry.
    private JsonObject? TypePatch(JsonObject? imported)
    {
        var type = IsClearRequested
            ? new JsonObject { [LabelMember] = null, [PluralMember] = null, [ColorMember] = null, [IconMember] = null }
            : (JsonObject?)imported?.DeepClone() ?? new JsonObject();

        if (Label is not null)
        {
            type[LabelMember] = ValueOf(Label);
        }

        if (Plural is not null)
        {
            type[PluralMember] = ValueOf(Plural);
        }

        if (Color is not null)
        {
            type[ColorMember] = ValueOf(Color.Trim());
        }

        if (Icon is not null)
        {
            type[IconMember] = ValueOf(ProjectSettingsDocuments.ToIconName(Icon));
        }

        return IsClearRequested && type.All(member => member.Value is null) ? null : type;
    }

    // A settings document, checked by the shared rules; null after a message when it cannot be read or breaks a rule.
    private async Task<Settings?> ReadImportAsync(string path)
    {
        var fullPath = _fileSystem.Path.GetFullPath(path);

        if (!_fileSystem.File.Exists(path))
        {
            Console.WriteImportant($"The file '{fullPath}' does not exist.");
            return null;
        }

        ProjectSettingsCheck check;

        try
        {
            var text = await _fileSystem.File.ReadAllTextAsync(path);
            check = ProjectSettingsJson.Read(JsonNode.Parse(text, documentOptions: FileOptions));
        }
        catch (JsonException exception)
        {
            Console.WriteImportant($"The file '{fullPath}' is not JSON: {exception.Message}");
            return null;
        }

        if (!check.IsValid)
        {
            Console.WriteImportant($"The file '{fullPath}' holds no valid project settings:");
            Console.WriteSettingsProblems(check.Problems);
            return null;
        }

        return check.Settings;
    }

    // An empty value removes the member (null in a merge patch).
    private static JsonNode? ValueOf(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : JsonValue.Create(value);
    }

    // The body of a PUT: the patch applied to nothing, so only its values (and no nulls) are left. The API checks them.
    private static SdkSettings ToBody(JsonObject document)
    {
        var types = new Dictionary<string, SdkAppearance>();

        if (document[TypesMember] is JsonObject typesObject)
        {
            foreach (var (code, value) in typesObject)
            {
                if (value is JsonObject appearance)
                {
                    types[code] = new SdkAppearance
                    {
                        Label = TextOf(appearance[LabelMember]),
                        Plural = TextOf(appearance[PluralMember]),
                        Color = TextOf(appearance[ColorMember]),
                        Icon = TextOf(appearance[IconMember]),
                    };
                }
            }
        }

        return new SdkSettings
        {
            Types = types,
            DefaultTimeZone = TextOf(document[DefaultTimeZoneMember]),
            DateFormat = TextOf(document[DateFormatMember]),
        };
    }

    private static string? TextOf(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }
}
