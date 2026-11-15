using System.Linq;
using _42.Platform.Cli.Telemetry;
using McMaster.Extensions.CommandLineUtils;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Telemetry;

// The usage of a run in its reports, from the real command tree of sform: the command path and the names of the options
// given, read where Program reads them (the root's OnParsingComplete, or the command of a failed parse). Never a value.
public class CommandUsageTests
{
    [Fact]
    public void ASecretValue_GivesThePathAndTheOptionNames_AndNoValue()
    {
        var usage = Record("story", "kv", "set", "db.password", "s3cr3t-value", "--secret", "-p", "acme.billing");

        usage.Command.ShouldBe("sform storyteller kv set");
        usage.Options.ShouldBe(["--secret", "--projectKey"], ignoreOrder: true);
        usage.ToString().ShouldStartWith("sform storyteller kv set --");
        usage.ToString().ShouldNotContain("s3cr3t");
        usage.ToString().ShouldNotContain("acme");
        usage.ToString().ShouldNotContain("db.password");
    }

    [Fact]
    public void AnOptionWithItsValueInTheToken_IsNamedByTheModel()
    {
        var usage = Record("story", "kv", "set", "key", "value", "--projectKey=acme.billing", "-v:prod");

        usage.Options.ShouldBe(["--projectKey", "--view"], ignoreOrder: true);
    }

    [Fact]
    public void AnAliasAndAShortName_GiveThePrimaryAndTheLongName()
    {
        var usage = Record("annotations", "kv", "create", "key", "value", "-p", "acme.billing");

        usage.ToString().ShouldBe("sform storyteller kv set --projectKey");
    }

    [Fact]
    public void Help_IsAnOptionGiven()
    {
        Record("story", "kv", "get", "--help").ToString().ShouldBe("sform storyteller kv get --help");
    }

    [Fact]
    public void TheDefaultOfAProperty_IsNotAnOptionGiven()
    {
        // story export has -f|--format = "folder" and --timeout = 10: the model writes them into Values after the root.
        var run = new CommandRun();
        var app = SformApplication.Create(run);
        app.Parse("story", "export", "-o", "out.zip");

        run.Usage!.ToString().ShouldBe("sform storyteller export --output");

        // Read after the handlers, the defaults are there.
        var export = app.Commands.Single(command => command.Name == "storyteller").Commands.Single(command => command.Name == "export");
        export.Options.Single(option => option.LongName == "format").HasValue().ShouldBeTrue();
        CommandUsage.Of(export).Options.ShouldBe(["--output"]);
    }

    [Fact]
    public void AnUnknownCommand_IsTheDeepestCommandMatched_WithoutTheToken()
    {
        var exception = Should.Throw<UnrecognizedCommandParsingException>(() => SformApplication.Create().Parse("story", "kv", "sett", "key", "s3cr3t-value"));

        var usage = CommandUsage.Of(exception.Command);

        usage.ToString().ShouldBe("sform storyteller kv");
        exception.Message.ShouldContain("sett");
    }

    [Fact]
    public void AnUnknownOption_KeepsTheOptionsBeforeIt()
    {
        var exception = Should.Throw<UnrecognizedCommandParsingException>(
            () => SformApplication.Create().Parse("story", "kv", "set", "key", "value", "--secret", "--pasword=hunter2"));

        var usage = CommandUsage.Of(exception.Command);

        usage.ToString().ShouldBe("sform storyteller kv set --secret");
    }

    [Fact]
    public void NoCommand_IsUnknown()
    {
        CommandUsage.Of(null).ShouldBe(CommandUsage.Unknown);
        CommandUsage.Unknown.ToString().ShouldBe(CommandPath.UNKNOWN);
    }

    [Fact]
    public void ANameOutsideTheRule_IsLeftOut()
    {
        var root = new CommandLineApplication { Name = "sform" };
        var renamed = root.Option("--renamed", "renamed after the parse", CommandOptionType.SingleValue);
        root.Option("--fine", "a fine name", CommandOptionType.NoValue);
        root.Parse("--renamed", "x", "--fine");

        renamed.LongName = "with value";

        CommandUsage.Of(root).ToString().ShouldBe("sform --fine");
    }

    [Fact]
    public void EveryOptionOfTheTree_HasANameOfTheRule()
    {
        var names = Descendants(SformApplication.Create())
            .SelectMany(command => command.Options)
            .Select(option => option.LongName is { Length: > 0 } longName ? $"--{longName}" : $"-{option.ShortName ?? option.SymbolName}")
            .Distinct()
            .ToList();

        names.Count.ShouldBeGreaterThan(50);
        names.ShouldAllBe(name => System.Text.RegularExpressions.Regex.IsMatch(name, "^--?[A-Za-z0-9?][A-Za-z0-9-]{0,63}$"));
    }

    private static CommandUsage Record(params string[] args)
    {
        var run = new CommandRun();
        SformApplication.Create(run).Parse(args);
        return run.Usage.ShouldNotBeNull();
    }

    private static System.Collections.Generic.IEnumerable<CommandLineApplication> Descendants(CommandLineApplication command)
    {
        return new[] { command }.Concat(command.Commands.SelectMany(Descendants));
    }
}
