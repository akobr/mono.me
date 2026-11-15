using System;
using System.Collections.Generic;
using _42.Platform.Cli.Configuration;
using Microsoft.Extensions.Options;

namespace _42.Platform.Cli.Services.Projects;

// The switches of a run that say whether and how the console output shows the project settings: --builtin (recorded by
// Program once the command line is parsed), then SFORM_PROJECT_SETTINGS and SFORM_ICONS, then the output section
// (output.projectSettings and output.icons of sform account settings, SFORM_output__*). A value that is not one of the
// switch's values counts as not set.
public sealed class OutputSwitches
{
    // The inherited root option of one run: the built-in names and dates (PlatformCommand).
    public const string BUILTIN_OPTION = "builtin";

    private readonly OutputOptions _options;

    public OutputSwitches(IOptions<OutputOptions> options)
        : this(options.Value, Environment.GetEnvironmentVariable)
    {
    }

    internal OutputSwitches(OutputOptions options, Func<string, string?> getVariable)
    {
        _options = options;
        GetVariable = getVariable;
    }

    // The environment the switches and the terminal detection read.
    public Func<string, string?> GetVariable { get; }

    // --builtin was given.
    public bool IsBuiltInRequested { get; set; }

    // auto, always or never.
    public string Icons => Choose(SformVariables.ICONS, _options.Icons, OutputOptions.IconsValues, OutputOptions.ICONS_AUTO);

    // False with --builtin, or with builtin from SFORM_PROJECT_SETTINGS or output.projectSettings.
    public bool UsesProjectSettings => !IsBuiltInRequested
        && Choose(SformVariables.PROJECT_SETTINGS, _options.ProjectSettings, OutputOptions.ProjectSettingsValues, OutputOptions.PROJECT_SETTINGS_PROJECT) == OutputOptions.PROJECT_SETTINGS_PROJECT;

    private string Choose(string variable, string? configured, IReadOnlyList<string> values, string defaultValue)
    {
        return OutputOptions.Parse(GetVariable(variable), values)
            ?? OutputOptions.Parse(configured, values)
            ?? defaultValue;
    }
}
