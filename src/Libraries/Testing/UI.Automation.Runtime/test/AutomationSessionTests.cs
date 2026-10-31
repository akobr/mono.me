using System.IO.Abstractions.TestingHelpers;
using _42.Testing.UI.Automation.Runtime.Access;
using _42.Testing.UI.Automation.Runtime.Browsers;
using _42.Testing.UI.Automation.Runtime.Reporting;
using _42.Testing.UI.Automation.Runtime.Settings;
using _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;
using _42.Testing.UI.Automation.Runtime.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace _42.Testing.UI.Automation.Runtime.UnitTests;

public sealed class AutomationSessionTests
{
    private static readonly string BaseDirectory = MockUnixSupport.Path(@"C:\suite\bin");

    private readonly MockFileSystem files = new();
    private readonly FakeTimeProvider time = new(TestData.Start);
    private readonly FakeBrowserProvider browserProvider = new();
    private readonly FakeReporterProvider reporterProvider = new("Testiny");

    [Fact]
    public async Task StartAsync_DescribesTheRun_AndWritesTheResultsFile()
    {
        var settings = Load("""
            {
              "Target": { "BaseUrl": "https://user:pw@admin.example.test", "SignIn": "None" },
              "Execution": { "Seed": 1234 },
              "Users": { "Pool": [ { "Email": "ada@example.test" } ] },
              "Reporting": { "Enabled": true, "RunName": "Nightly {Date} {Engine} {Branch} {Sha7}", "Testiny": { "ApiKey": "key" } }
            }
            """);
        var planned = TestData.Identity("Planned", new TestCaseLink("1", null));

        await using var session = await Start(settings, planned);

        session.RunId.ShouldBe("20261031-142500-abcd");
        session.Seed.ShouldBe(1234);
        session.SignInFlow.ShouldBeNull();
        session.StorageStates.ShouldBeNull();
        session.Run.Name.ShouldBe("Nightly 2026-10-31 chromium feat/424-133 268a67a");
        session.Run.Properties[TestRunProperties.Target].ShouldBe("https://admin.example.test/");
        session.Run.Properties[TestRunProperties.Seed].ShouldBe("1234");
        session.Run.Properties[TestRunProperties.BrowserVersion].ShouldBe("142.0");
        session.Run.Properties[TestRunProperties.BuildUrl].ShouldBe("https://github.com/akobr/mono.me/actions/runs/1");
        session.Run.Properties[TestRunProperties.PlaywrightVersion].ShouldBe(AutomationSession.PlaywrightVersion);
        session.Run.Description.ShouldNotBeNull().ShouldContain("| Seed | 1234 |");
        session.Run.Description.ShouldNotContain("pw");
        session.RunDirectory.ShouldBe(files.Path.Combine(BaseDirectory, "TestResults", "artifacts", "20261031-142500-abcd"));
        files.File.ReadAllLines(files.Path.Combine(session.RunDirectory, ResultsFile.FileName)).ShouldHaveSingleItem().ShouldContain("\"kind\":\"run\"");

        var reporter = reporterProvider.Created.ShouldHaveSingleItem();
        reporter.StartedRun.ShouldBeSameAs(session.Run);
        reporter.PlannedTests.ShouldHaveSingleItem().ShouldBe(planned);
        reporterProvider.Section.ShouldNotBeNull()["ApiKey"].ShouldBe("key");
    }

    [Fact]
    public async Task StartAsync_HandsTheProvidersTheRunsConfiguration_AndTheConsumersServices()
    {
        var settings = Load("""
            {
              "Target": { "BaseUrl": "https://admin.example.test", "SignIn": "None" },
              "Reporting": { "Enabled": true, "UnmappedTests": "Warn", "Testiny": { "ApiKey": "key" } }
            }
            """);
        var options = Options();
        var logger = new Mock<ILogger>();
        options.Logger = logger.Object;
        var consumerServices = new Mock<IServiceProvider>();
        consumerServices.Setup(services => services.GetService(typeof(TimeProvider))).Returns(time);
        options.Services = consumerServices.Object;

        await using var session = await AutomationSession.StartAsync(settings, options);

        var services = reporterProvider.Services.ShouldNotBeNull();
        var configuration = services.GetService(typeof(IConfiguration)).ShouldBeAssignableTo<IConfiguration>().ShouldNotBeNull();
        configuration["Reporting:UnmappedTests"].ShouldBe("Warn");
        services.GetService(typeof(TimeProvider)).ShouldBeSameAs(time);
        services.GetService(typeof(ILoggerFactory)).ShouldBeAssignableTo<ILoggerFactory>().ShouldNotBeNull().CreateLogger("Testiny").ShouldBeSameAs(logger.Object);
    }

    [Fact]
    public async Task StartAsync_UsesTheFirstSignInFlow_WhenTargetSignInIsUnset()
    {
        var settings = Load("""{ "Target": { "BaseUrl": "https://admin.example.test" } }""");
        var flow = new FakeSignInFlow("AuthKit");

        await using var session = await Start(settings, signInFlows: [flow, new FakeSignInFlow("Other")]);

        session.SignInFlow.ShouldBeSameAs(flow);
        session.StorageStates.ShouldNotBeNull().Flow.ShouldBeSameAs(flow);
        reporterProvider.Created.ShouldBeEmpty();
    }

    [Fact]
    public async Task StartAsync_FailsInSkipMode()
    {
        var settings = Load("{}");

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => Start(settings));

        exception.Message.ShouldStartWith("UITESTS_Target__BaseUrl is not configured.");
    }

    [Fact]
    public async Task StartAsync_FailsForAReporterThatRefusesItsSettings_AndClosesTheBrowsers()
    {
        var settings = Load("""{ "Target": { "BaseUrl": "https://admin.example.test", "SignIn": "None" }, "Reporting": { "Enabled": true, "Testiny": { "ProjectId": 1 } } }""");

        var exception = await Should.ThrowAsync<UiAutomationSettingsException>(() => Start(settings));

        exception.Message.ShouldBe("Reporting:Testiny: ApiKey is required.");
        browserProvider.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task StartAsync_DisposesTheReportersItCreated_WhenALaterProviderRefusesItsSettings()
    {
        var other = new FakeReporterProvider("Other");
        var settings = Load("""
            {
              "Target": { "BaseUrl": "https://admin.example.test", "SignIn": "None" },
              "Reporting": { "Enabled": true, "Systems": [ "Testiny", "Other" ], "Testiny": { "ApiKey": "key" }, "Other": { "ProjectId": 1 } }
            }
            """);
        var options = Options();
        options.ReporterProviders = [reporterProvider, other];

        var exception = await Should.ThrowAsync<UiAutomationSettingsException>(() => AutomationSession.StartAsync(settings, options));

        exception.Message.ShouldBe("Reporting:Other: ApiKey is required.");
        var created = reporterProvider.Created.ShouldHaveSingleItem();
        created.Disposed.ShouldBeTrue();
        created.StartedRun.ShouldBeNull();
        browserProvider.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task StartAsync_FailsForAPoolThatCannotLease_BeforeABrowserOrAReporterStarts()
    {
        var settings = new UiAutomationSettings
        {
            Target = new TargetSettings { BaseUrl = "https://admin.example.test", SignIn = TargetSettings.NoSignIn },
            Users = new UsersSettings { Pool = [new PoolUserSettings { Email = " " }] },
            Reporting = new ReportingSettings { Enabled = true },
        };
        var options = Options();
        var launched = false;
        options.BrowserProviderFactory = (_, _) =>
        {
            launched = true;
            return Task.FromResult<IBrowserProvider>(browserProvider);
        };

        await Should.ThrowAsync<ArgumentException>(() => AutomationSession.StartAsync(settings, options));

        launched.ShouldBeFalse();
        reporterProvider.Created.ShouldBeEmpty();
    }

    [Fact]
    public async Task StartAsync_FailsForASystemNoProviderHas()
    {
        var settings = Load("""{ "Target": { "BaseUrl": "https://admin.example.test", "SignIn": "None" }, "Reporting": { "Enabled": true, "Systems": [ "TestRail" ] } }""");

        var exception = await Should.ThrowAsync<UiAutomationSettingsException>(() => Start(settings));

        exception.Message.ShouldBe("Reporting:Systems names 'TestRail', but no reporter provider is registered for it; registered: Testiny.");
    }

    [Fact]
    public async Task StartAsync_FailsForAnUnknownDevice()
    {
        var settings = Load("""{ "Target": { "BaseUrl": "https://admin.example.test", "SignIn": "None" }, "Browser": { "Device": "Nokia 3310" } }""");

        var exception = await Should.ThrowAsync<UiAutomationSettingsException>(() => Start(settings));

        exception.Message.ShouldBe("Browser:Device 'Nokia 3310' is not a Playwright device descriptor.");
        browserProvider.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task StartAsync_HandsTheProviderTheRunValues_AndThePlannedTests()
    {
        var settings = Load("""{ "Target": { "BaseUrl": "https://admin.example.test", "SignIn": "None" }, "Browser": { "Device": "Pixel 7" } }""");
        var planned = TestData.Identity("Planned");
        var devices = new DeviceCatalog(new Dictionary<string, Microsoft.Playwright.BrowserNewContextOptions> { ["Pixel 7"] = new() });
        BrowserProviderContext? received = null;
        var options = Options();
        options.PlannedTests = [planned];
        options.BrowserProviderFactory = (context, _) =>
        {
            received = context;
            return Task.FromResult<IBrowserProvider>(new FakeBrowserProvider(devices));
        };

        await using var session = await AutomationSession.StartAsync(settings, options);

        received.ShouldNotBeNull();
        received.Settings.ShouldBeSameAs(settings);
        received.PlannedTests.ShouldHaveSingleItem().ShouldBe(planned);
        received.Run.RunId.ShouldBe("20261031-142500-abcd");
        received.Run.Engine.ShouldBe("chromium");
        received.Run.Device.ShouldBe("Pixel 7");
        received.Run.Branch.ShouldBe("feat/424-133");
        received.Run.BuildNumber.ShouldBe("1");
        received.Run.Machine.ShouldBe("runner");
        session.Run.Properties[TestRunProperties.Device].ShouldBe("Pixel 7");
    }

    [Fact]
    public async Task CreateReport_BlocksATestWithoutAUser_AndAddsTheSessionProperties()
    {
        var settings = Load("""{ "Target": { "BaseUrl": "https://admin.example.test", "SignIn": "None" }, "Execution": { "Seed": 7 } }""");
        await using var session = await Start(settings);

        var report = session.CreateReport(
            TestData.Identity("Test"),
            TestOutcome.Failed,
            TestData.Start,
            TimeSpan.FromSeconds(2),
            attempt: 2,
            new UserLeaseException("No free user with role Owner within 120 s; the pool has 1, 1 leased."),
            properties: new Dictionary<string, string> { [TestRunProperties.User] = "u-12345678" });

        report.Outcome.ShouldBe(TestOutcome.Blocked);
        report.Failure.ShouldNotBeNull().ExceptionType.ShouldBe(typeof(UserLeaseException).FullName);
        report.Properties[TestRunProperties.Seed].ShouldBe("7");
        report.Properties[TestRunProperties.Attempt].ShouldBe("2");
        report.Properties[TestRunProperties.Engine].ShouldBe("chromium");
        report.Properties[TestRunProperties.User].ShouldBe("u-12345678");
        report.Output.ShouldBeNull();
    }

    [Fact]
    public async Task CreateReport_KeepsOnlyTheReasonOfASkip()
    {
        var settings = Load("""{ "Target": { "BaseUrl": "https://admin.example.test", "SignIn": "None" } }""");
        await using var session = await Start(settings);

        var report = session.CreateReport(
            TestData.Identity("Skipped"),
            TestOutcome.Skipped,
            TestData.Start,
            TimeSpan.Zero,
            attempt: 1,
            new InvalidOperationException("Requires Browser:Provider BrowserStack; the run uses Local."));

        report.Outcome.ShouldBe(TestOutcome.Skipped);
        report.Failure.ShouldBe(new TestFailure("Requires Browser:Provider BrowserStack; the run uses Local.", null, null, null, null));
    }

    [Fact]
    public async Task StopAsync_CompletesTheReportersWithTheSettingsWarnings_AndClosesTheBrowsers()
    {
        var settings = Load("""
            {
              "Target": { "BaseUrl": "https://admin.example.test", "SignIn": "None" },
              "Users": { "Pool": [ { "Email": "ada@example.test" } ] },
              "Reporting": { "Enabled": true, "Testiny": { "ApiKey": "key" } }
            }
            """);
        var session = await Start(settings);

        session.Report(session.CreateReport(TestData.Identity("Passes"), TestOutcome.Passed, TestData.Start, TimeSpan.FromSeconds(1), 1));
        var summary = await session.StopAsync();

        summary.Passed.ShouldBe(1);
        summary.Warnings.ShouldContain("Execution:MaxParallelTests 4 is more than the 1 users of Users:Pool; lowered to 1.");
        reporterProvider.Created.ShouldHaveSingleItem().CompletedSummary.ShouldNotBeNull().Passed.ShouldBe(1);
        browserProvider.Disposed.ShouldBeTrue();
        (await session.StopAsync()).ShouldBeSameAs(summary);
    }

    [Fact]
    public async Task Report_TellsTheBrowserProviderThatTheTestIsDone_AndSurvivesItsFailure()
    {
        var settings = Load("""{ "Target": { "BaseUrl": "https://admin.example.test", "SignIn": "None" } }""");
        await using var session = await Start(settings);
        var skipped = TestData.Identity("Skipped");
        var blocked = TestData.Identity("Blocked");

        session.Report(session.CreateReport(skipped, TestOutcome.Skipped, TestData.Start, TimeSpan.Zero, 1, new InvalidOperationException("Requires WebKit.")));
        browserProvider.FailCompleted = new InvalidOperationException("Boom");
        session.Report(session.CreateReport(blocked, TestOutcome.Blocked, TestData.Start, TimeSpan.Zero, 1));

        browserProvider.Completed.ShouldBe([skipped, blocked]);
        var summary = await session.StopAsync();
        summary.Skipped.ShouldBe(1);
        summary.Blocked.ShouldBe(1);
    }

    [Fact]
    public async Task ReplayAsync_SendsAResultsFileThroughTheReporters()
    {
        var resultsPath = files.Path.Combine(BaseDirectory, "old", ResultsFile.FileName);
        var file = new ResultsFile(files, resultsPath);
        var planned = TestData.Identity("Replayed", new TestCaseLink("9", null));
        file.WriteRun(TestData.Run("20261030-101010-wxyz"), [planned]);
        file.Append(TestData.Report(planned, TestOutcome.Failed));
        var settings = Load("""{ "Reporting": { "Enabled": true, "ReplayFile": "old/results.jsonl", "Testiny": { "ApiKey": "key" } } }""");

        var result = await AutomationSession.ReplayAsync(settings, Options());

        result.Results.ShouldBe(1);
        result.Summary.Failed.ShouldBe(1);
        result.RunUrls["Testiny"].ShouldBe(new Uri("https://reports.example.test/runs/20261030-101010-wxyz"));
        var reporter = reporterProvider.Created.ShouldHaveSingleItem();
        reporter.StartedRun.ShouldNotBeNull().RunId.ShouldBe("20261030-101010-wxyz");
        reporter.Results.ShouldHaveSingleItem().Test.MethodName.ShouldBe("Replayed");
        reporter.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task ReplayAsync_NamesAMissingFile()
    {
        var settings = Load("""{ "Reporting": { "Enabled": true, "ReplayFile": "missing.jsonl", "Testiny": { "ApiKey": "key" } } }""");

        var exception = await Should.ThrowAsync<UiAutomationSettingsException>(() => AutomationSession.ReplayAsync(settings, Options()));

        exception.Message.ShouldBe($"Reporting:ReplayFile {files.Path.Combine(BaseDirectory, "missing.jsonl")} does not exist.");
    }

    [Fact]
    public void NewRunId_IsTheTimeAndFourCharacters()
    {
        AutomationSession.NewRunId(TestData.Start, new Random(1)).ShouldMatch("^20261031-142500-[a-z0-9]{4}$");
    }

    private UiAutomationSettings Load(string json)
    {
        files.AddFile(files.Path.Combine(BaseDirectory, "testsettings.json"), new MockFileData(json));
        return UiAutomationSettingsLoader.Load(new UiAutomationSettingsOptions
        {
            BaseDirectory = BaseDirectory,
            FileSystem = files,
            EnvironmentVariables = new Dictionary<string, string?>(),
        });
    }

    private Task<AutomationSession> Start(UiAutomationSettings settings, TestIdentity? planned = null, IReadOnlyList<ISignInFlow>? signInFlows = null)
    {
        var options = Options();
        options.PlannedTests = planned is null ? [] : [planned];
        options.SignInFlows = signInFlows ?? [];
        return AutomationSession.StartAsync(settings, options);
    }

    private AutomationSessionOptions Options()
    {
        return new AutomationSessionOptions
        {
            ReporterProviders = [reporterProvider],
            FileSystem = files,
            TimeProvider = time,
            RunId = "20261031-142500-abcd",
            TestEngine = "xUnit",
            Environment = new RunEnvironment("feat/424-133", "268a67a2e4f1c0de", "1", "https://github.com/akobr/mono.me/actions/runs/1", "runner"),
            BrowserProviderFactory = (_, _) => Task.FromResult<IBrowserProvider>(browserProvider),
        };
    }
}
