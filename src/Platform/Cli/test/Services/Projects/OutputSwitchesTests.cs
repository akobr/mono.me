using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Commands.Projects;
using _42.Platform.Cli.Commands.Storyteller;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Services.Projects;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Services.Projects;

// The switches of the console output (spec section 7.4): --builtin for one run, SFORM_PROJECT_SETTINGS and SFORM_ICONS,
// then output.projectSettings and output.icons; and what the settings never change: the input of the commands.
public class OutputSwitchesTests
{
    [Theory]
    [InlineData(null, null, "auto")]
    [InlineData(null, "always", "always")]
    [InlineData("never", "always", "never")]
    [InlineData(" Always ", null, "always")]
    [InlineData("sometimes", "never", "never")]
    [InlineData("sometimes", "sometimes", "auto")]
    public void Icons_TheVariableWinsOverTheOptions(string? variable, string? configured, string expected)
    {
        Switches(configured, null, (SformVariables.ICONS, variable)).Icons.ShouldBe(expected);
    }

    [Theory]
    [InlineData(false, null, null, true)]
    [InlineData(true, null, null, false)]
    [InlineData(true, "project", "project", false)]
    [InlineData(false, "builtin", null, false)]
    [InlineData(false, "project", "builtin", true)]
    [InlineData(false, null, "BUILTIN", false)]
    [InlineData(false, "other", "builtin", false)]
    public void ProjectSettings_AreUsedUnlessSwitchedOff(bool builtInFlag, string? variable, string? configured, bool expected)
    {
        var switches = Switches(null, configured, (SformVariables.PROJECT_SETTINGS, variable));
        switches.IsBuiltInRequested = builtInFlag;

        switches.UsesProjectSettings.ShouldBe(expected);
    }

    // --builtin is an option of the root that every command inherits, so it can follow any command.
    [Fact]
    public void Builtin_IsAnInheritedOptionOfTheRoot()
    {
        var option = typeof(PlatformCommand).GetProperty(nameof(PlatformCommand.IsBuiltInRequested))!.GetCustomAttribute<OptionAttribute>().ShouldNotBeNull();

        option.Template.ShouldBe("--builtin");
        option.Inherited.ShouldBeTrue();
    }

    [Theory]
    [InlineData(new[] { "project", "--builtin" }, true)]
    [InlineData(new[] { "--builtin", "project" }, true)]
    [InlineData(new[] { "project" }, false)]
    public void Builtin_IsFoundOnTheSelectedCommand(string[] args, bool expected)
    {
        var app = new CommandLineApplication<PlatformCommand>(NullConsole.Singleton);
        app.Conventions.UseDefaultConventions().UseConstructorInjection(new ServiceCollection().BuildServiceProvider());

        var selected = app.Parse(args).SelectedCommand;

        selected.Name.ShouldBe(CommandNames.PROJECT);
        Program.IsBuiltInRequested(selected).ShouldBe(expected);
    }

    // Settings change what sform shows, never what it accepts or writes: the commands that build an annotation (story set
    // writes no TimeZone, whatever the project's default time zone) or parse a tree root never see them.
    [Theory]
    [InlineData(typeof(StorytellerSetCommand))]
    [InlineData(typeof(StorytellerEditCommand))]
    [InlineData(typeof(ProjectSettingsSetCommand))]
    public void TheCommandsOfInput_DoNotTakeTheProjectSettings(Type command)
    {
        command.GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .ShouldNotContain(parameter => parameter.ParameterType == typeof(IProjectSettingsProvider));
    }

    private static OutputSwitches Switches(string? icons, string? projectSettings, params (string Name, string? Value)[] variables)
    {
        var values = variables.ToDictionary(variable => variable.Name, variable => variable.Value, StringComparer.Ordinal);
        return new OutputSwitches(new OutputOptions { Icons = icons, ProjectSettings = projectSettings }, name => values.GetValueOrDefault(name));
    }
}
