using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Commands.Projects;
using _42.Platform.Cli.Configuration;
using _42.Platform.Storyteller;
using _42.Platform.Storyteller.Sdk;
using Alba.CsConsoleFormat;
using Newtonsoft.Json.Linq;
using BuiltInAppearance = _42.Platform.Storyteller.ProjectTypeAppearance;
using SdkAppearance = _42.Platform.Storyteller.Sdk.ProjectTypeAppearance;
using SdkSettings = _42.Platform.Storyteller.Sdk.ProjectSettings;

namespace _42.Platform.Cli.Output;

// The output of sform project settings: the table of the seven types and the other settings, the problems of a
// settings document, and how a failed read or write of the settings ends.
public static class ProjectSettingsConsoleExtensions
{
    internal const string Swatch = "■";
    internal const string None = "—";

    // The settings as people read them: per type its code, its built-in name, the project's name and plural, its color
    // (as a swatch in that color when colors are shown) and its icon, then the default time zone, the date format, the
    // version and the last change. What the project leaves to the built-in appearance or the defaults is shown dimmed.
    public static void WriteProjectSettings(this IExtendedConsole @this, string projectKey, SdkSettings settings, bool showColors)
    {
        var catalog = ProjectSettingsCatalog.Default;
        var theme = @this.Theme;
        var types = settings.Types ?? new Dictionary<string, SdkAppearance>();

        @this.WriteHeader($"Project settings of {projectKey}");
        @this.WriteTable(
            catalog.TypeCodes,
            code => TypeCells(code, Find(types, code), catalog.BuiltIn[code], theme, showColors),
            new IHeaderColumn[]
            {
                new HeaderColumn("Code"),
                new HeaderColumn("Type"),
                new HeaderColumn("Name"),
                new HeaderColumn("Plural"),
                new HeaderColumn("Color"),
                new HeaderColumn("Icon"),
            });

        @this.WriteLine(ValueOrDefault("Default time zone: ", settings.DefaultTimeZone, catalog.DefaultTimeZone, theme));
        @this.WriteLine(ValueOrDefault("Date format: ", settings.DateFormat, catalog.DefaultDateFormat, theme));
        @this.WriteLine($"Version: {settings.Version.ToString(CultureInfo.InvariantCulture)}");
        @this.WriteLine(settings.Modified is { } modified
            ? $"Modified: {AuditConsoleExtensions.FormatTime(modified.At)} by {AuditConsoleExtensions.FormatActor(modified.By)}"
            : "Modified: never");
        @this.WriteLine("Change them with sform project settings set, edit and reset.".ThemedLowlight(theme));
    }

    // Each problem as "<path>: <message>", as the API and the shared rules report them.
    public static void WriteSettingsProblems(this IExtendedConsole @this, IEnumerable<ProjectSettingsProblem> problems)
    {
        foreach (var problem in problems)
        {
            @this.WriteImportant($"{problem.Path}: {problem.Message}");
        }
    }

    public static void WriteChangedMeanwhile(this IExtendedConsole @this, long? version)
    {
        @this.WriteImportant(version is { } current
            ? $"The project settings changed meanwhile; they are at version {current.ToString(CultureInfo.InvariantCulture)}."
            : "The project settings changed meanwhile.");
    }

    // A failed write of the settings: a 412 names the version they are at, a 400 with the problems of the settings lists
    // them (both exit 2); anything else as every command ends an API error (a 403 with the server's message is 44).
    public static int WriteSettingsFailure(this IExtendedConsole @this, ApiException exception)
    {
        switch (exception.StatusCode)
        {
            case (int)HttpStatusCode.PreconditionFailed:
                @this.WriteChangedMeanwhile(ReadCurrentVersion(exception));
                return ExitCodes.ERROR_WRONG_INPUT;

            case (int)HttpStatusCode.BadRequest when ReadProblems(exception) is { Count: > 0 } problems:
                @this.WriteSettingsProblems(problems);
                return ExitCodes.ERROR_WRONG_INPUT;

            default:
                return @this.WriteApiError(exception);
        }
    }

    // Whether the table shows the colors of the types: on a console, unless NO_COLOR has a value.
    public static bool ShowsTypeColors(this IExtendedConsole @this, Func<string, string?> getVariable)
    {
        return @this is SformConsole { HasConsole: true }
            && string.IsNullOrEmpty(getVariable(SformVariables.NO_COLOR));
    }

    // The problems of an InvalidSettings answer (Details: Path, Rule, Message); null when it has none.
    internal static IReadOnlyList<ProjectSettingsProblem>? ReadProblems(ApiException exception)
    {
        if (ReadDetails(exception) is not JArray details)
        {
            return null;
        }

        return details
            .OfType<JObject>()
            .Select(problem => new ProjectSettingsProblem(
                Text(problem, nameof(ProjectSettingsProblem.Path)) ?? "$",
                Text(problem, nameof(ProjectSettingsProblem.Rule)) ?? string.Empty,
                Text(problem, nameof(ProjectSettingsProblem.Message)) ?? string.Empty))
            .ToList();
    }

    // The version of a SettingsPreconditionFailed answer (Details: Version); null when it has none.
    internal static long? ReadCurrentVersion(ApiException exception)
    {
        return ReadDetails(exception) is JObject details
            && details.GetValue(nameof(SdkSettings.Version), StringComparison.OrdinalIgnoreCase) is JValue { Type: JTokenType.Integer } version
                ? version.Value<long>()
                : null;
    }

    internal static IEnumerable<Cell> TypeCells(string code, SdkAppearance? own, BuiltInAppearance builtIn, IConsoleTheme theme, bool showColors)
    {
        var label = string.IsNullOrWhiteSpace(own?.Label) ? null : own.Label;
        var plural = string.IsNullOrWhiteSpace(own?.Plural) ? null : own.Plural;

        yield return CellOf(code);
        yield return CellOf(builtIn.Label!);
        yield return CellOf(label is null ? None.ThemedLowlight(theme) : new Span(label));
        yield return CellOf(plural is not null
            ? new Span(plural)
            : (label is null ? None : ProjectSettingsDocuments.Pluralize(label)).ThemedLowlight(theme));
        yield return ColorCell(own?.Color, builtIn.Color!, theme, showColors);
        yield return CellOf(own?.Icon is { Length: > 0 } icon ? new Span(icon) : builtIn.Icon!.ThemedLowlight(theme));
    }

    private static Cell ColorCell(string? own, string builtIn, IConsoleTheme theme, bool showColors)
    {
        var color = string.IsNullOrEmpty(own) ? builtIn : own;
        var name = string.IsNullOrEmpty(own) ? builtIn.ThemedLowlight(theme) : new Span(own);

        return showColors && TypeColors.TryGetConsoleColor(color, out var consoleColor)
            ? CellOf(Swatch.Colored(consoleColor), new Span(" "), name)
            : CellOf(name);
    }

    private static Cell CellOf(params object[] content)
    {
        return new Cell(content) { Stroke = LineThickness.None, Margin = new Thickness(1, 0) };
    }

    private static object[] ValueOrDefault(string name, string? value, string defaultValue, IConsoleTheme theme)
    {
        return string.IsNullOrWhiteSpace(value)
            ? [name, defaultValue, " (default)".ThemedLowlight(theme)]
            : [name, value];
    }

    private static SdkAppearance? Find(IDictionary<string, SdkAppearance> types, string code)
    {
        return types.FirstOrDefault(entry => string.Equals(entry.Key, code, StringComparison.OrdinalIgnoreCase)).Value;
    }

    private static JToken? ReadDetails(ApiException exception)
    {
        if (exception is ApiException<ErrorResponse> { Result.Details: { } details })
        {
            return details as JToken ?? JToken.FromObject(details);
        }

        if (string.IsNullOrWhiteSpace(exception.Response))
        {
            return null;
        }

        try
        {
            return JObject.Parse(exception.Response).GetValue("Details", StringComparison.OrdinalIgnoreCase);
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }

    private static string? Text(JObject item, string name)
    {
        return item.GetValue(name, StringComparison.OrdinalIgnoreCase) is JValue { Type: JTokenType.String } value
            ? value.Value<string>()
            : null;
    }
}
