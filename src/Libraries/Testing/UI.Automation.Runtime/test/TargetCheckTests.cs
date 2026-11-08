using System.IO.Abstractions.TestingHelpers;
using _42.Testing.UI.Automation.Runtime.Browsers;
using _42.Testing.UI.Automation.Runtime.Reporting;
using _42.Testing.UI.Automation.Runtime.Settings;
using _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Playwright;

namespace _42.Testing.UI.Automation.Runtime.UnitTests;

public sealed class TargetCheckTests
{
    private const string Settings = """
        {
          "Target": { "BaseUrl": "https://admin.example.test", "SignIn": "None" },
          "Execution": { "Trace": "Off" },
          "Users": { "Pool": [ { "Email": "ada@example.test", "Roles": [ "Owner" ] } ] },
          "Reporting": { "Enabled": true, "Testiny": { "ApiKey": "key" } }
        }
        """;

    private static readonly string BaseDirectory = MockUnixSupport.Path(@"C:\suite\bin");

    private readonly MockFileSystem files = new();
    private readonly FakeTimeProvider time = new(TestData.Start);
    private readonly FakeBrowserProvider browserProvider = new() { Browser = new FakeRemoteBrowser().Object };
    private readonly FakeReporterProvider reporterProvider = new("Testiny");

    [Fact]
    public async Task StartAsync_RunsTheCheckOnASignedInPage_BeforeAReporterCreatesItsRun()
    {
        var settings = Load(Settings);
        TargetCheckContext? checkedWith = null;
        TestRunDescriptor? runAtCheck = null;
        var leasedAtCheck = -1;
        var options = Options((context, _) =>
        {
            checkedWith = context;
            runAtCheck = reporterProvider.Created.ShouldHaveSingleItem().StartedRun;
            leasedAtCheck = context.Session.Users.LeasedCount;
            return Task.CompletedTask;
        });
        options.PlannedTests = [TestData.Identity("Planned")];

        await using var session = await AutomationSession.StartAsync(settings, options);

        var context = checkedWith.ShouldNotBeNull();
        context.Session.ShouldBeSameAs(session);
        context.Settings.ShouldBeSameAs(settings);
        context.User.Email.ShouldBe("ada@example.test");
        context.Page.ShouldNotBeNull();
        context.Scope.Test.ShouldBe(TargetCheckContext.Test);
        runAtCheck.ShouldBeNull();
        leasedAtCheck.ShouldBe(1);

        session.Users.LeasedCount.ShouldBe(0);
        browserProvider.Acquired.ShouldBe([TargetCheckContext.Test]);
        browserProvider.Released.ShouldHaveSingleItem().ShouldNotBeNull().Outcome.ShouldBe(TestOutcome.Passed);
        browserProvider.Completed.ShouldBe([TargetCheckContext.Test]);
        reporterProvider.Created.ShouldHaveSingleItem().StartedRun.ShouldBeSameAs(session.Run);
        reporterProvider.Created[0].PlannedTests.ShouldHaveSingleItem().MethodName.ShouldBe("Planned");
    }

    [Fact]
    public async Task StartAsync_TellsTheProviderOfTheCheck_AmongThePlannedTests()
    {
        var settings = Load(Settings);
        BrowserProviderContext? received = null;
        var options = Options((_, _) => Task.CompletedTask);
        options.PlannedTests = [TestData.Identity("Planned")];
        options.BrowserProviderFactory = (context, _) =>
        {
            received = context;
            return Task.FromResult<IBrowserProvider>(browserProvider);
        };

        await using var session = await AutomationSession.StartAsync(settings, options);

        received.ShouldNotBeNull().PlannedTests.Select(test => test.MethodName).ShouldBe(["Planned", "CheckTarget"]);
    }

    [Fact]
    public async Task StartAsync_FailsWithTheChecksMessage_AndNoReporterCreatesARun()
    {
        var settings = Load(Settings);
        var options = Options((_, _) => throw new TargetCheckException("Target:Project 'nope' was not found in organization 'acme'."));

        var exception = await Should.ThrowAsync<TargetCheckException>(() => AutomationSession.StartAsync(settings, options));

        exception.Message.ShouldBe("The target check failed: Target:Project 'nope' was not found in organization 'acme'.");
        exception.InnerException.ShouldBeOfType<TargetCheckException>();
        var reporter = reporterProvider.Created.ShouldHaveSingleItem();
        reporter.StartedRun.ShouldBeNull();
        reporter.Disposed.ShouldBeTrue();
        browserProvider.Released.ShouldHaveSingleItem().ShouldNotBeNull().Outcome.ShouldBe(TestOutcome.Failed);
        browserProvider.Disposed.ShouldBeTrue();
        files.FileExists(files.Path.Combine(BaseDirectory, "TestResults", "artifacts", "20261031-142500-abcd", ResultsFile.FileName)).ShouldBeFalse();
    }

    [Fact]
    public async Task StartAsync_NamesTheTypeOfAnyOtherFailure()
    {
        var settings = Load(Settings);
        var options = Options((_, _) => throw new InvalidOperationException("Locator expected to be visible."));

        var exception = await Should.ThrowAsync<TargetCheckException>(() => AutomationSession.StartAsync(settings, options));

        exception.Message.ShouldBe("The target check failed: InvalidOperationException: Locator expected to be visible.");
    }

    [Fact]
    public async Task StartAsync_ShowsASecretInTheChecksFailure_AsStars_AndDropsTheExceptionThatHeldIt()
    {
        var settings = Load("""
            {
              "Target": { "BaseUrl": "https://admin.example.test", "SignIn": "None" },
              "Execution": { "Trace": "Off" },
              "Users": { "Pool": [ { "Email": "ada@example.test", "Password": "pool-password-1" } ] }
            }
            """);
        var options = Options((_, _) => throw new InvalidOperationException("Call log:\n  -   fill(\"pool-password-1\") for ada@example.test"));

        var exception = await Should.ThrowAsync<TargetCheckException>(() => AutomationSession.StartAsync(settings, options));

        exception.Message.ShouldStartWith("The target check failed: InvalidOperationException: Call log:");
        exception.Message.ShouldContain("fill(\"***\") for ***");
        exception.InnerException.ShouldBeNull();
        exception.ToString().ShouldNotContain("pool-password-1");
        exception.ToString().ShouldNotContain("ada@example.test");
    }

    [Fact]
    public async Task StartAsync_FailsTheCheck_WhenItsScopeCannotBegin()
    {
        var settings = Load(Settings);
        var called = false;
        var options = Options((_, _) =>
        {
            called = true;
            return Task.CompletedTask;
        });
        options.BrowserProviderFactory = (_, _) => Task.FromResult<IBrowserProvider>(new FakeBrowserProvider());

        var exception = await Should.ThrowAsync<TargetCheckException>(() => AutomationSession.StartAsync(settings, options));

        exception.Message.ShouldBe("The target check failed: NotSupportedException: The fake provider has no browser.");
        called.ShouldBeFalse();
        reporterProvider.Created.ShouldHaveSingleItem().StartedRun.ShouldBeNull();
    }

    [Fact]
    public async Task StartAsync_LetsACancelledCheckEndTheStart()
    {
        var settings = Load(Settings);
        using var cancellation = new CancellationTokenSource();
        var options = Options(async (_, token) =>
        {
            await cancellation.CancelAsync();
            token.ThrowIfCancellationRequested();
        });

        await Should.ThrowAsync<OperationCanceledException>(() => AutomationSession.StartAsync(settings, options, cancellation.Token));

        browserProvider.Released.ShouldHaveSingleItem().ShouldNotBeNull().Outcome.ShouldBe(TestOutcome.Cancelled);
        browserProvider.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task StartAsync_RunsNoCheck_WithoutOne()
    {
        var settings = Load(Settings);
        var options = Options(check: null);

        await using var session = await AutomationSession.StartAsync(settings, options);

        browserProvider.Acquired.ShouldBeEmpty();
        reporterProvider.Created.ShouldHaveSingleItem().StartedRun.ShouldBeSameAs(session.Run);
    }

    [Fact]
    public async Task StartAsync_RunsACheckWithoutAPage_WithItsUser_AndStartsNoBrowserProvider()
    {
        var settings = Load(Settings);
        var factoryCalls = 0;
        TargetCheckContext? checkedWith = null;
        var leasedAtCheck = -1;
        var options = Options((context, _) =>
        {
            checkedWith = context;
            leasedAtCheck = context.Session.Users.LeasedCount;
            return Task.CompletedTask;
        });
        options.TargetCheckUsesBrowser = false;
        options.BrowserProviderStart = BrowserProviderStart.OnFirstLease;
        options.BrowserProviderFactory = (_, _) =>
        {
            factoryCalls++;
            return Task.FromResult<IBrowserProvider>(browserProvider);
        };

        await using var session = await AutomationSession.StartAsync(settings, options);

        var context = checkedWith.ShouldNotBeNull();
        context.User.Email.ShouldBe("ada@example.test");
        leasedAtCheck.ShouldBe(1);
        context.HasPage.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => context.Page).Message
            .ShouldBe("The target check has no page: the setup's TargetCheckUsesBrowser is false. Open a browser context when the check needs one (OpenBrowserAsync, or Scope.OpenBrowserAsync for another user).");
        Should.Throw<InvalidOperationException>(() => context.Context).Message.ShouldStartWith("The target check has no context");
        factoryCalls.ShouldBe(0);
        session.Users.LeasedCount.ShouldBe(0);
        reporterProvider.Created.ShouldHaveSingleItem().StartedRun.ShouldBeSameAs(session.Run);
    }

    [Fact]
    public async Task StartAsync_LetsACheckWithoutAPage_OpenABrowserOnDemand()
    {
        var settings = Load(Settings);
        var factoryCalls = 0;
        IBrowserContext? opened = null;
        var options = Options(async (context, token) => opened = await context.OpenBrowserAsync(token));
        options.TargetCheckUsesBrowser = false;
        options.BrowserProviderStart = BrowserProviderStart.OnFirstLease;
        options.BrowserProviderFactory = (_, _) =>
        {
            factoryCalls++;
            return Task.FromResult<IBrowserProvider>(browserProvider);
        };

        await using var session = await AutomationSession.StartAsync(settings, options);

        opened.ShouldNotBeNull();
        factoryCalls.ShouldBe(1);
        browserProvider.Acquired.ShouldBe([TargetCheckContext.Test]);
        browserProvider.Released.ShouldHaveSingleItem().ShouldNotBeNull().Outcome.ShouldBe(TestOutcome.Passed);
        browserProvider.Completed.ShouldBe([TargetCheckContext.Test]);
    }

    [Fact]
    public async Task StartAsync_FailsACheckWithoutAPage_ThatOpensABrowserThatDoesNotStart()
    {
        var settings = Load(Settings);
        var options = Options(async (context, token) => await context.OpenBrowserAsync(token));
        options.TargetCheckUsesBrowser = false;
        options.BrowserProviderStart = BrowserProviderStart.OnFirstLease;
        options.BrowserProviderFactory = (_, _) => throw new BrowserUnavailableException("The chromium browser did not launch.");

        var exception = await Should.ThrowAsync<TargetCheckException>(() => AutomationSession.StartAsync(settings, options));

        exception.Message.ShouldBe("The target check failed: BrowserUnavailableException: The chromium browser did not launch.");
    }

    [Fact]
    public void FromSetup_TakesWhetherTheCheckUsesABrowser()
    {
        AutomationSessionOptions.FromSetup(new CheckingSetup()).TargetCheckUsesBrowser.ShouldBeTrue();
        AutomationSessionOptions.FromSetup(new CheckingWithoutPageSetup()).TargetCheckUsesBrowser.ShouldBeFalse();
    }

    [Fact]
    public async Task FromSetup_TakesTheCheckOfASetupThatImplementsIt()
    {
        var setup = new CheckingSetup();
        var options = AutomationSessionOptions.FromSetup(setup);

        await options.TargetCheck.ShouldNotBeNull()(null!, CancellationToken.None);

        setup.Checks.ShouldBe(1);
        AutomationSessionOptions.FromSetup(new ExplicitlyCheckingSetup()).TargetCheck.ShouldNotBeNull();
        AutomationSessionOptions.FromSetup(new DerivedCheckingSetup()).TargetCheck.ShouldNotBeNull();
    }

    [Fact]
    public void FromSetup_TakesNoCheck_FromASetupWithTheDefault()
    {
        AutomationSessionOptions.FromSetup(new FakeSetup()).TargetCheck.ShouldBeNull();
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

    private AutomationSessionOptions Options(Func<TargetCheckContext, CancellationToken, Task>? check)
    {
        return new AutomationSessionOptions
        {
            ReporterProviders = [reporterProvider],
            FileSystem = files,
            TimeProvider = time,
            RunId = "20261031-142500-abcd",
            Environment = new RunEnvironment(null, null, null, null, "runner"),
            BrowserProviderFactory = (_, _) => Task.FromResult<IBrowserProvider>(browserProvider),
            TargetCheck = check,
        };
    }

    private class CheckingSetup : IUiAutomationSetup
    {
        public int Checks { get; private set; }

        public string ReadmeHint => "README.md";

        public Task CheckTargetAsync(TargetCheckContext context, CancellationToken cancellationToken)
        {
            Checks++;
            return Task.CompletedTask;
        }
    }

    private sealed class DerivedCheckingSetup : CheckingSetup;

    private sealed class CheckingWithoutPageSetup : CheckingSetup, IUiAutomationSetup
    {
        public bool TargetCheckUsesBrowser => false;
    }

    private sealed class ExplicitlyCheckingSetup : IUiAutomationSetup
    {
        public string ReadmeHint => "README.md";

        Task IUiAutomationSetup.CheckTargetAsync(TargetCheckContext context, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
