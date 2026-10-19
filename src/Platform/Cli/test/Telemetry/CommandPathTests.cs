using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Telemetry;
using McMaster.Extensions.CommandLineUtils;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Telemetry;

// The command path of the usage event, from the real command tree of sform: primary names only, never an argument.
public class CommandPathTests
{
    [Fact]
    public void AnAlias_ReportsThePrimaryNames_AndNoArgument()
    {
        var result = CreateApplication().Parse("story", "kv", "get", "some-key");

        CommandPath.Of(result.SelectedCommand).ShouldBe("sform storyteller kv get");
    }

    [Fact]
    public void Login_ReportsAccount()
    {
        var result = CreateApplication().Parse("login");

        CommandPath.Of(result.SelectedCommand).ShouldBe("sform account");
    }

    [Fact]
    public void EveryCommandOfTheTree_GivesAPathOfSafeNames()
    {
        var paths = Descendants(CreateApplication()).Select(CommandPath.Of).ToList();

        paths.Count.ShouldBeGreaterThan(50);
        paths.ShouldAllBe(path => Regex.IsMatch(path, "^sform( [a-z][a-z0-9-]{0,31}){0,7}$"));
    }

    [Fact]
    public void NoCommand_IsUnknown()
    {
        CommandPath.Of(null).ShouldBe(CommandPath.UNKNOWN);
    }

    [Theory]
    [InlineData("Get")]
    [InlineData("get some-key")]
    [InlineData("1get")]
    [InlineData("get.key")]
    [InlineData("a-name-that-is-longer-than-32-chars")]
    public void ANameOutsideTheRule_IsUnknown(string name)
    {
        var root = new CommandLineApplication { Name = "sform" };
        var command = root.Command(name, _ => { });

        CommandPath.Of(command).ShouldBe(CommandPath.UNKNOWN);
    }

    [Fact]
    public void MoreThanEightLevels_IsUnknown()
    {
        var command = new CommandLineApplication { Name = "sform" };
        var levels = new List<CommandLineApplication> { command };

        for (var level = 2; level <= 9; level++)
        {
            command = command.Command($"level{level}", _ => { });
            levels.Add(command);
        }

        CommandPath.Of(levels[7]).ShouldBe("sform level2 level3 level4 level5 level6 level7 level8");
        CommandPath.Of(levels[8]).ShouldBe(CommandPath.UNKNOWN);
    }

    [Fact]
    public void RecordParsing_TellsHelpAndVersionFromARun()
    {
        var run = new CommandRun();

        run.RecordParsing(CreateApplication().Parse("story", "kv", "get", "--help").SelectedCommand);
        (run.Command, run.Information).ShouldBe(("sform storyteller kv get", CommandOutcome.HELP));

        run.RecordParsing(CreateApplication().Parse("--version").SelectedCommand);
        (run.Command, run.Information).ShouldBe(("sform", CommandOutcome.VERSION));

        run.RecordParsing(CreateApplication().Parse("story", "kv", "get", "some-key").SelectedCommand);
        (run.Command, run.Information).ShouldBe(("sform storyteller kv get", null));
    }

    // As the host builds it, without its services: every constructor dependency is a mock.
    private static CommandLineApplication<PlatformCommand> CreateApplication()
    {
        var app = new CommandLineApplication<PlatformCommand>(NullConsole.Singleton);
        app.Conventions.UseDefaultConventions().UseConstructorInjection(new MockingServiceProvider());
        return app;
    }

    private static IEnumerable<CommandLineApplication> Descendants(CommandLineApplication command)
    {
        return new[] { command }.Concat(command.Commands.SelectMany(Descendants));
    }

    private sealed class MockingServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IServiceProvider))
            {
                return this;
            }

            return serviceType.IsInterface || serviceType.IsAbstract
                ? ((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(serviceType))!).Object
                : null;
        }
    }
}
