using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Running;

namespace _42.Platform.Cli.AutomationTests.Tests.Account;

// What sform project settings prints (ProjectSettingsCommand, ProjectSettingsConsoleExtensions, ProjectSettingsCommands of
// the CLI), read from the plain standard output of a run:
//   - the table: "# Project settings of <org>.<project>", the header "Code  Type  Name  Plural  Color  Icon" and a row per
//     type code (its cells apart by two spaces or more), then "Default time zone: …", "Date format: …", "Version: <n>",
//     "Modified: …" and "Change them with sform project settings set, edit and reset.";
//   - the outcome of a write: "# Changes" and a line-numbered diff of the file shape, then "Saved the project settings,
//     version <n>." or "The project settings did not change; they stay at version <n>.";
//   - -f json: the settings as the API answers them (Types, DefaultTimeZone, DateFormat, Version, Modified), and -e the
//     file shape (the Format marker, Types, DefaultTimeZone, DateFormat).
// The catalog of the values is src/Platform/Storyteller/conformance/project-settings.json.
internal static partial class ProjectSettingsOutput
{
    // The marker of the file shape (ProjectSettings.FileFormat).
    public const string FileFormat = "2s.project-settings.v1";

    public const string ChangeHint = "Change them with sform project settings set, edit and reset.";

    // The API's refusal of a machine's write (ProjectSettingsHttp.ChangingSettingsNeedsUser).
    public const string MachineRefusal = "Changing the project settings needs a user with the Administrator role.";

    // The type codes in the catalog's order with their built-in names.
    public static IReadOnlyList<(string Code, string Name)> BuiltInTypes { get; } =
    [
        ("rst", "Responsibility"),
        ("unt", "Unit"),
        ("sbt", "Subject"),
        ("usg", "Usage"),
        ("cnt", "Context"),
        ("exe", "Execution"),
        ("uxe", "Unit of execution"),
    ];

    // The colors of the catalog, in its order.
    public static IReadOnlyList<string> Colors { get; } =
    [
        "red", "orange", "amber", "yellow", "lime", "green", "emerald", "teal", "cyan", "sky", "blue", "indigo", "violet",
        "purple", "fuchsia", "pink", "rose", "gray",
    ];

    // The icons of the catalog, in its order.
    public static IReadOnlyList<string> Icons { get; } =
    [
        "i-lucide-target", "i-lucide-box", "i-lucide-sun", "i-lucide-link", "i-lucide-layers", "i-lucide-play", "i-lucide-cpu",
        "i-lucide-activity", "i-lucide-anchor", "i-lucide-atom", "i-lucide-bell", "i-lucide-bookmark", "i-lucide-bot",
        "i-lucide-boxes", "i-lucide-brain", "i-lucide-briefcase", "i-lucide-bug", "i-lucide-building-2", "i-lucide-calendar",
        "i-lucide-circle", "i-lucide-clock", "i-lucide-cloud", "i-lucide-code", "i-lucide-cog", "i-lucide-compass",
        "i-lucide-component", "i-lucide-container", "i-lucide-crown", "i-lucide-database", "i-lucide-factory", "i-lucide-file",
        "i-lucide-flag", "i-lucide-flame", "i-lucide-folder", "i-lucide-gauge", "i-lucide-gem", "i-lucide-git-branch",
        "i-lucide-globe", "i-lucide-heart", "i-lucide-hexagon", "i-lucide-key", "i-lucide-landmark", "i-lucide-layout-grid",
        "i-lucide-leaf", "i-lucide-lightbulb", "i-lucide-map-pin", "i-lucide-monitor", "i-lucide-network", "i-lucide-package",
        "i-lucide-puzzle", "i-lucide-rocket", "i-lucide-server", "i-lucide-shield", "i-lucide-smartphone", "i-lucide-square",
        "i-lucide-star", "i-lucide-store", "i-lucide-tag", "i-lucide-triangle", "i-lucide-truck", "i-lucide-user",
        "i-lucide-users", "i-lucide-workflow", "i-lucide-wrench", "i-lucide-zap",
    ];

    // The date formats of the catalog, in its order.
    public static IReadOnlyList<string> DateFormats { get; } = ["yyyy-mm-dd", "dd.mm.yyyy", "dd/mm/yyyy", "mm/dd/yyyy", "d mmm yyyy"];

    public static string Saved(long version) => $"Saved the project settings, version {Text(version)}.";

    public static string Unchanged(long version) => $"The project settings did not change; they stay at version {Text(version)}.";

    public static string ChangedMeanwhile(long version) => $"The project settings changed meanwhile; they are at version {Text(version)}.";

    public static string DryRun(long version) => $"Dry run: nothing was saved; the project settings stay at version {Text(version)}.";

    public static string WouldNotChange(long version) => $"The project settings would not change; they stay at version {Text(version)}.";

    // The API's refusal of a user without the Administrator role (EnsureRole of the API).
    public static string NoAdministratorAccess(string projectKey) => $"No Administrator access to '{projectKey}'.";

    // The cells of the row of the type code in the table: code, type, name, plural, color, icon. Null when the table has
    // no such row.
    public static IReadOnlyList<string>? TypeRow(SformResult result, string code)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return result.Lines
            .Select(line => Columns().Split(line.Trim()))
            .FirstOrDefault(cells => cells.Length == 6 && cells[0] == code);
    }

    // Whether the table has its header row.
    public static bool HasHeader(SformResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Lines.Any(line => HeaderRow().IsMatch(line));
    }

    // The settings of -f json (or a file): their version.
    public static long VersionOf(JsonNode settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings["Version"]?.GetValue<long>() ?? throw new InvalidOperationException($"The settings have no Version: {settings.ToJsonString()}");
    }

    // The appearance of a type in the settings (Types.<code>), or null.
    public static JsonObject? TypeOf(JsonNode settings, string code)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings["Types"]?[code] as JsonObject;
    }

    // A text member of a type's appearance (Label, Plural, Color, Icon), or null.
    public static string? TypeValue(JsonNode settings, string code, string member)
    {
        return TypeOf(settings, code)?[member]?.GetValue<string>();
    }

    // The type codes the settings name, in their order.
    public static IReadOnlyList<string> TypeCodes(JsonNode settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return (settings["Types"] as JsonObject)?.Select(pair => pair.Key).ToList() ?? [];
    }

    // A text member of the settings (DefaultTimeZone, DateFormat, Format), or null.
    public static string? Value(JsonNode settings, string member)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings[member]?.GetValue<string>();
    }

    // A line of the changes: an added ('+') or removed ('-') line of the diff, "<n> │ + <line>" ("│" may arrive as "?" in
    // the code page of a Windows console), e.g. HasChange(result, '+', "\"Icon\": \"i-lucide-box\"").
    public static bool HasChange(SformResult result, char sign, string text)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var pattern = $@"^[ \d]*[│|?�] {Regex.Escape(sign.ToString())} .*{Regex.Escape(text)},?\s*$";
        return Regex.IsMatch(result.PlainStdOut, pattern, RegexOptions.Multiline | RegexOptions.CultureInvariant);
    }

    private static string Text(long version) => version.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Columns();

    [GeneratedRegex(@"^\s*Code\s+Type\s+Name\s+Plural\s+Color\s+Icon\s*$")]
    private static partial Regex HeaderRow();
}
