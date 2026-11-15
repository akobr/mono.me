using System;
using System.IO;
using _42.Platform.Cli.Telemetry;
using McMaster.Extensions.CommandLineUtils;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Telemetry;

// The values a run was given, which its reports must not show: of the model and of args, with their parts; only the
// project and the view may stay in the local log.
public class CommandLineValuesTests
{
    [Fact]
    public void TheModel_GivesTheArgumentsAndTheOptionValues_AndTheProjectAsALocalValue()
    {
        var values = Record("story", "kv", "set", "db.password", "s3cr3t-value", "--secret", "-p", "acme.billing", "-v", "prod");

        values.Hidden.ShouldContain("db.password");
        values.Hidden.ShouldContain("s3cr3t-value");
        values.Hidden.ShouldContain("password");
        values.Hidden.ShouldNotContain("acme.billing");
        values.Hidden.ShouldNotContain("prod");
        values.LocalValues.ShouldBe([new("--projectKey", "acme.billing"), new("--view", "prod")], ignoreOrder: true);
        values.All.ShouldContain("acme.billing");
        values.All.ShouldContain("acme");
        values.All.ShouldContain("billing");
        values.All.ShouldContain("prod");
        values.All.ShouldContain("s3cr3t-value");
    }

    [Fact]
    public void AMultipleValueOption_GivesEveryValueAndItsParts()
    {
        var values = Record("story", "config", "set", "exe.northwind.invoicing.prod", "-x", "db.password=hunter2", "-x", "db.port=5432");

        values.Hidden.ShouldContain("db.password=hunter2");
        values.Hidden.ShouldContain("hunter2");
        values.Hidden.ShouldContain("db.port=5432");
        values.Hidden.ShouldContain("5432");
        values.Hidden.ShouldContain("northwind");
        values.LocalValues.ShouldBeEmpty();
    }

    [Fact]
    public void AResponseFile_GivesTheValuesItHolds()
    {
        var file = Path.Combine(Path.GetTempPath(), $"sform-values-{Guid.NewGuid():N}.rsp");
        File.WriteAllLines(file, ["story", "kv", "set", "rsp-key", "rsp secret value", "--secret"]);

        try
        {
            var run = new CommandRun();
            string[] args = [$"@{file}"];
            run.RecordArguments(args);
            SformApplication.Create(run).Parse(args);

            run.Usage!.ToString().ShouldBe("sform storyteller kv set --secret");
            run.Values.Hidden.ShouldContain("rsp secret value");
            run.Values.Hidden.ShouldContain("rsp-key");
            run.Values.Hidden.ShouldContain(file);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Args_GiveEveryTokenThatIsNotAnOptionName()
    {
        var values = CommandLineValues.FromArguments(["story", "kv", "sett", "--pasword=hunter2", "--secret", "-v:prod", "-pacme.billing", "@C:\\in\\args.rsp", "--", "after"]);

        values.Hidden.ShouldContain("sett");
        values.Hidden.ShouldContain("hunter2");
        values.Hidden.ShouldContain("prod");
        values.Hidden.ShouldContain("acme.billing");
        values.Hidden.ShouldContain("C:\\in\\args.rsp");
        values.Hidden.ShouldContain("args");
        values.Hidden.ShouldContain("after");
        values.Hidden.ShouldNotContain("--secret");
        values.Hidden.ShouldNotContain("secret");
        values.Hidden.ShouldNotContain("--");
    }

    [Fact]
    public void AValueOfAnOptionOutsideTheList_IsHidden()
    {
        var values = Record("story", "export", "-o", "C:\\Users\\ada\\out.zip");

        values.Hidden.ShouldContain("C:\\Users\\ada\\out.zip");
        values.Hidden.ShouldNotContain("Users");
        values.LocalValues.ShouldBeEmpty();
    }

    [Fact]
    public void AFailedParse_GivesTheValuesBeforeTheFailure()
    {
        var exception = Should.Throw<UnrecognizedCommandParsingException>(
            () => SformApplication.Create().Parse("story", "kv", "set", "key", "s3cr3t-value", "-p", "acme.billing", "--pasword=hunter2"));

        var values = CommandLineValues.Of(exception.Command);

        values.Hidden.ShouldContain("s3cr3t-value");
        values.LocalValues.ShouldBe([new("--projectKey", "acme.billing")]);
        values.All.ShouldNotContain("hunter2");
    }

    [Fact]
    public void Merge_KeepsBoth_WithoutTheNamesOfTheCommands_AndALocalValueStaysLocal()
    {
        var run = new CommandRun();
        string[] args = ["story", "kv", "set", "key", "s3cr3t-value", "-p", "acme.billing"];
        run.RecordArguments(args);
        SformApplication.Create(run).Parse(args);

        run.Values.Hidden.ShouldContain("s3cr3t-value");
        run.Values.Hidden.ShouldContain("key");
        run.Values.Hidden.ShouldNotContain("story");
        run.Values.Hidden.ShouldNotContain("kv");
        run.Values.Hidden.ShouldNotContain("set");
        run.Values.Hidden.ShouldNotContain("acme.billing");
        run.Values.All.ShouldContain("acme.billing");
    }

    [Fact]
    public void AValueThatIsAlsoACommandName_StaysAValueOfTheModel()
    {
        var run = new CommandRun();
        string[] args = ["story", "kv", "get", "story"];
        run.RecordArguments(args);
        SformApplication.Create(run).Parse(args);

        run.Values.Hidden.ShouldBe(["story"]);
    }

    [Fact]
    public void AnUnknownCommand_StaysAValue()
    {
        var run = new CommandRun();
        string[] args = ["story", "kv", "sett", "key"];
        run.RecordArguments(args);
        var exception = Should.Throw<UnrecognizedCommandParsingException>(() => SformApplication.Create(run).Parse(args));
        run.RecordFailedParsing(exception.Command);

        run.Values.Hidden.ShouldBe(["sett", "key"], ignoreOrder: true);
    }

    [Fact]
    public void BothSidesOfAnInlineProperty_AreValues_WhateverTheirLength()
    {
        var values = Record("story", "config", "set", "exe.northwind.invoicing.prod", "-x", "db.password=x9/k2;Qz", "-x", "db.port=543");

        values.Hidden.ShouldContain("x9/k2;Qz");
        values.Hidden.ShouldContain("db.password");
        values.Hidden.ShouldContain("543");
        values.Hidden.ShouldContain("db.port");
    }

    [Fact]
    public void WithParts_GivesTheLines_NoSchemeOfAUrl_AndTheNamesOfAFullKey()
    {
        CommandLineValues.WithParts("first-line-secret\nsecond line").ShouldBe(["first-line-secret\nsecond line", "first-line-secret", "second line"], ignoreOrder: true);
        CommandLineValues.WithParts("https://keys.example.org/jwks").ShouldNotContain("https:");
        CommandLineValues.WithParts("@acme.billing.prod.sbt.app").ShouldContain("acme");
        CommandLineValues.WithParts("@acme.billing.prod.sbt.app").ShouldContain("billing");
    }

    // A file path gives its segments, its file name and its stem, not the common folders of a profile.
    [Fact]
    public void WithParts_OfAFilePath_GivesItsSegmentsButNotTheCommonFolders()
    {
        CommandLineValues.WithParts("out/acme-billing.env").ShouldContain("acme-billing");
        CommandLineValues.WithParts("contoso.example/api").ShouldContain("contoso");
        CommandLineValues.WithParts(@"C:\Users\ada\plans\out.zip").ShouldContain("plans");
        CommandLineValues.WithParts(@"C:\Users\ada\plans\out.zip").ShouldNotContain("Users");
        CommandLineValues.WithParts("api://contoso-storyteller/.default").ShouldNotContain("default");
    }

    [Fact]
    public void NothingGiven_IsEmpty()
    {
        CommandLineValues.Of(null).All.ShouldBeEmpty();
        CommandLineValues.FromArguments(null).All.ShouldBeEmpty();
        CommandLineValues.FromArguments(["--secret", "-p", string.Empty]).All.ShouldBeEmpty();
    }

    private static CommandLineValues Record(params string[] args)
    {
        var run = new CommandRun();
        SformApplication.Create(run).Parse(args);
        return run.Values;
    }
}
