using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Credentials;
using _42.Platform.Storyteller.Sdk.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Playground.DotNet.SimpleExecutor.UnitTests;

public class ExecutorTests
{
    private const string Responsibility = "rst.playground";
    private const string NorthwindExecution = "exe.northwind.playground.production";
    private const string NorthwindUnit = "uxe.northwind.playground.production.nightly-export";

    [Fact]
    public async Task Who_RendersTheKeyAndItsAncestors()
    {
        await using var api = await FakeApi.StartAsync();

        var run = await RunAsync(api, NorthwindExecution, "who");

        run.ExitCode.ShouldBe(ExecutorCommand.Succeeded);
        run.Output.ShouldContain($"Who am I: {FakeApi.Scope}.{NorthwindExecution}");
        run.Output.ShouldContain("Type: Execution");
        run.Output.ShouldContain("Status: Active");
        run.Output.ShouldContain("Ancestors: rst.playground, sbt.northwind, usg.northwind.playground, cnt.northwind.production");
    }

    [Fact]
    public async Task All_OnAResponsibilitysKey_RendersEveryExecutionUnitAndUnitOfExecution()
    {
        await using var api = await FakeApi.StartAsync();

        var run = await RunAsync(api, Responsibility, "--context", "production");

        run.ExitCode.ShouldBe(ExecutorCommand.Succeeded);
        run.Output.ShouldContain($"Who am I: {FakeApi.Scope}.{Responsibility}");
        run.Output.ShouldContain("Added exe.contoso.playground.production: Active");
        run.Output.ShouldContain($"Added {NorthwindExecution}: Active");
        run.Output.ShouldContain("Usage usg.northwind.playground: Active, plan=gold");
        run.Output.ShouldContain("Units: nightly-export");
        run.Output.ShouldContain($"--- {NorthwindExecution}, version 1, yaml");
        run.Output.ShouldContain("--- uxe.contoso.playground.production.nightly-export, version 1, yaml");
        run.Output.ShouldContain($"--- {NorthwindUnit}, version 1, yaml");
        run.Output.ShouldContain("target: \"northwind-archive\"");
    }

    [Fact]
    public async Task All_AUnitSharedBySubjects_IsRenderedOnce()
    {
        await using var api = await FakeApi.StartAsync();

        var run = await RunAsync(api, Responsibility, "all", "--context", "production");

        run.Output.Split("--- unt.playground.nightly-export,").Length.ShouldBe(2);
    }

    [Theory]
    [InlineData("executions", "exe.")]
    [InlineData("units", "unt.")]
    [InlineData("units-of-execution", "uxe.")]
    public async Task What_RendersThatKindOnly(string what, string typeCode)
    {
        await using var api = await FakeApi.StartAsync();

        var run = await RunAsync(api, Responsibility, what, "--context", "production");
        var documents = run.Output.Split('\n').Where(line => line.StartsWith("--- ", StringComparison.Ordinal)).ToList();

        run.ExitCode.ShouldBe(ExecutorCommand.Succeeded);
        documents.ShouldNotBeEmpty();
        documents.ShouldAllBe(line => line.StartsWith($"--- {typeCode}", StringComparison.Ordinal));
        run.Output.ShouldNotContain("Who am I");
    }

    [Theory]
    [InlineData("flat", "\"target\": \"northwind-archive\"")]
    [InlineData("properties", "target=northwind-archive")]
    [InlineData("env", "TARGET=northwind-archive")]
    [InlineData("yaml", "target: \"northwind-archive\"")]
    public async Task Format_WritesTheDocumentInIt(string format, string line)
    {
        await using var api = await FakeApi.StartAsync();

        var run = await RunAsync(api, NorthwindExecution, "units-of-execution", "--format", format);

        run.ExitCode.ShouldBe(ExecutorCommand.Succeeded);
        run.Output.ShouldContain($"--- {NorthwindUnit}, version 1, {format}");
        run.Output.ShouldContain(line);
    }

    [Fact]
    public async Task All_OnAnExecutionsKey_RendersThatExecutionOnly_WithItsContext()
    {
        await using var api = await FakeApi.StartAsync();

        var run = await RunAsync(api, NorthwindExecution);

        run.ExitCode.ShouldBe(ExecutorCommand.Succeeded);
        run.Output.ShouldContain($"Execution {NorthwindExecution}: Active");
        run.Output.ShouldContain("Usage usg.northwind.playground: Active, plan=gold");
        run.Output.ShouldContain($"--- {NorthwindUnit}, version 1, yaml");
        run.Output.ShouldNotContain("contoso");
    }

    [Fact]
    public async Task All_OnAUsagesKey_KeepsItsSubject()
    {
        await using var api = await FakeApi.StartAsync();

        var run = await RunAsync(api, "usg.northwind.playground", "--context", "production");

        run.ExitCode.ShouldBe(ExecutorCommand.Succeeded);
        run.Output.ShouldContain($"Added {NorthwindExecution}: Active");
        run.Output.ShouldNotContain("contoso");
    }

    [Fact]
    public async Task Units_OnAUnitsKey_NeedsNoContext()
    {
        await using var api = await FakeApi.StartAsync();

        var run = await RunAsync(api, "unt.playground.nightly-export", "units");

        run.ExitCode.ShouldBe(ExecutorCommand.Succeeded);
        run.Output.ShouldContain("--- unt.playground.nightly-export, version 1, yaml");
        run.Output.ShouldContain("schedule: \"0 2 * * *\"");
    }

    [Fact]
    public async Task ADocumentThatFails_DoesNotStopTheOthers_AndFailsTheRun()
    {
        await using var api = await FakeApi.StartAsync();
        api.RemoveConfiguration("unt.playground.nightly-export");

        var run = await RunAsync(api, Responsibility, "--context", "production");

        run.ExitCode.ShouldBe(ExecutorCommand.Failed);
        run.Error.ShouldContain("! unt.playground.nightly-export:");
        run.Output.ShouldContain($"--- {NorthwindUnit}, version 1, yaml");
    }

    [Fact]
    public async Task ADocumentAFormatCannotWrite_NamesThePath_AndFailsTheRun()
    {
        await using var api = await FakeApi.StartAsync();
        api.AddConfiguration(NorthwindUnit, """{ "pass word": "secret value" }""");

        var run = await RunAsync(api, NorthwindExecution, "units-of-execution", "--format", "env");

        run.ExitCode.ShouldBe(ExecutorCommand.Failed);
        run.Error.ShouldContain($"! {NorthwindUnit}:");
        run.Error.ShouldContain("$['pass word']");
        run.Error.ShouldNotContain("secret value");
    }

    [Fact]
    public async Task ATitleAndUsageValuesWithLineEnds_StayOnTheirLines()
    {
        await using var api = await FakeApi.StartAsync();
        api.AddAnnotation(NorthwindExecution, new JObject { ["Title"] = "title\n  Status: Forged", ["UnitNames"] = new JArray() });
        api.AddAnnotation("usg.northwind.playground", new JObject { ["Values"] = new JObject { ["plan"] = "gold\nForged line" } });

        var run = await RunAsync(api, NorthwindExecution);

        run.Output.ShouldContain("  Title: title\\n  Status: Forged");
        run.Output.ShouldContain("Usage usg.northwind.playground: Active, plan=gold\\nForged line");
    }

    [Fact]
    public async Task AKeyThatDoesNotExist_FailsWithItsMessage()
    {
        await using var api = await FakeApi.StartAsync();

        var run = await RunAsync(api, "rst.missing", "who");

        run.ExitCode.ShouldBe(ExecutorCommand.Failed);
        run.Error.ShouldContain("rst.missing");
    }

    [Fact]
    public async Task AContextWithoutExecutions_SaysSo()
    {
        await using var api = await FakeApi.StartAsync();

        var run = await RunAsync(api, Responsibility, "executions", "--context", "staging");

        run.ExitCode.ShouldBe(ExecutorCommand.Succeeded);
        run.Output.ShouldContain("No active execution of playground in staging.");
    }

    [Theory]
    [InlineData(Responsibility, "nonsense", "--context", "production")]
    [InlineData(Responsibility, "all", "--context", "production", "--format", "xml")]
    [InlineData(Responsibility, "all", "--context", "not.one.segment")]
    [InlineData(Responsibility, "all")]
    [InlineData(Responsibility, "all", "--unknown")]
    [InlineData(NorthwindExecution, "all", "--context", "production")]
    [InlineData("sbt.northwind", "executions")]
    public async Task InputItRefuses_ExitsWithWrongInput(string key, params string[] arguments)
    {
        await using var api = await FakeApi.StartAsync();

        var run = await RunAsync(api, key, arguments);

        run.ExitCode.ShouldBe(ExecutorCommand.WrongInput);
        run.Error.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task ASubjectsKey_RendersWho_ThenRefusesTheRest()
    {
        await using var api = await FakeApi.StartAsync();

        var run = await RunAsync(api, "sbt.northwind");

        run.Output.ShouldContain($"Who am I: {FakeApi.Scope}.sbt.northwind");
        run.ExitCode.ShouldBe(ExecutorCommand.WrongInput);
    }

    [Fact]
    public async Task SettingsTheRegistrationRefuses_ExitWithWrongInput()
    {
        var console = new FakeConsole();

        var exitCode = await Program.RunAsync(
            [],
            console,
            services => services.AddStoryteller(options => options.Endpoint = "ftp://not.an.endpoint/api"));

        exitCode.ShouldBe(ExecutorCommand.WrongInput);
        console.ErrorWriter.ToString().ShouldNotBeEmpty();
    }

    // Program.RunAsync, as Main runs it. The settings that decide a test are in code, and no proxy of the machine running
    // the tests stands between them and the fake.
    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(FakeApi api, string annotationKey, params string[] arguments)
    {
        var console = new FakeConsole();

        var exitCode = await Program.RunAsync(
            arguments,
            console,
            services => services.AddStoryteller(options =>
            {
                options.Endpoint = api.Endpoint;
                options.Key = $"{FakeApi.Scope}.{annotationKey}";
                options.Mode = StorytellerReadMode.Resolved;
                options.Credential = StorytellerCredential.FromApiKey("2s.machine.secret");
                options.ConfigurePrimaryHandler = handler => handler.UseProxy = false;
                options.Resilience = new StorytellerResilienceOptions { BaseDelay = TimeSpan.Zero, MaxRetryAttempts = 0, MinimumThroughput = 1000 };
            }));

        return (exitCode, console.OutWriter.ToString(), console.ErrorWriter.ToString());
    }
}
