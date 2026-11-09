using System.IO.Abstractions.TestingHelpers;
using _42.Testing.UI.Automation.Runtime.Browsers;
using _42.Testing.UI.Automation.Runtime.Reporting;
using _42.Testing.UI.Automation.Runtime.Settings;
using _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Playwright;
using Moq;

namespace _42.Testing.UI.Automation.Runtime.UnitTests;

// A scope without a browser (TestScopeOptions.Browser false) leases its user and has its data and artifacts, but
// acquires a browser only when the test asks for one (OpenBrowserAsync), from a provider that starts on the first lease.
public sealed class TestScopeWithoutBrowserTests
{
    private const string Settings = """
        {
          "Target": { "BaseUrl": "https://api.example.test/api", "SignIn": "AuthKit" },
          "Execution": { "Trace": "Always", "Screenshot": "Always", "Video": "Always" },
          "Users": { "Pool": [ { "Email": "ada@example.test", "Password": "pw-1" }, { "Email": "grace@example.test", "Password": "pw-2" } ] }
        }
        """;

    private static readonly string BaseDirectory = MockUnixSupport.Path(@"C:\suite\bin");

    private readonly MockFileSystem files = new();
    private readonly FakeTimeProvider time = new(TestData.Start);
    private readonly FakeSignInFlow flow = new(signInItemPrefix: "workos:");
    private readonly ListLogger logger = new();
    private readonly Mock<IBrowser> browser = new();
    private readonly List<string?> startedFrom = [];
    private readonly List<Mock<IBrowserContext>> contexts = [];
    private readonly FakeBrowserProvider provider;
    private int factoryCalls;
    private int contextsClosed;
    private string state = State("R1");

    public TestScopeWithoutBrowserTests()
    {
        provider = new FakeBrowserProvider { Browser = browser.Object };
        browser.Setup(current => current.NewContextAsync(It.IsAny<BrowserNewContextOptions?>())).Returns(async (BrowserNewContextOptions? options) =>
        {
            if (HoldContext is { } hold)
            {
                await hold();
            }

            lock (startedFrom)
            {
                startedFrom.Add(options?.StorageState);
            }

            return NewContext().Object;
        });
    }

    // Awaited by the browser before it hands out a new context, e.g. to end the scope while one opens.
    private Func<Task>? HoldContext { get; set; }

    // The provider of a session that started on the first lease, or the failure of its start.
    private Func<Task<IBrowserProvider>>? Factory { get; set; }

    [Fact]
    public async Task BeginAsync_LeasesTheUser_ButStartsAndAcquiresNoBrowser()
    {
        await using var session = await StartAsync();
        var scope = TestScope.Create(session, TestData.Identity("Command"), new TestScopeOptions { Browser = false });

        await scope.BeginAsync();

        scope.IsStarted.ShouldBeTrue();
        scope.UsesBrowser.ShouldBeFalse();
        scope.User.Email.ShouldBe("ada@example.test");
        session.Users.LeasedCount.ShouldBe(1);
        factoryCalls.ShouldBe(0);
        flow.SignIns.ShouldBe(0);
        Should.Throw<InvalidOperationException>(() => scope.Page).Message
            .ShouldBe("The test scope has no page: it runs without a browser (TestScopeOptions.Browser is false); OpenBrowserAsync opens a context, whose NewPageAsync opens one.");
        Should.Throw<InvalidOperationException>(() => scope.Context).Message.ShouldContain("TestScopeOptions.Browser is false");
        Should.Throw<InvalidOperationException>(() => scope.Browser).Message.ShouldContain("OpenBrowserAsync was not called");

        var report = await scope.EndAsync(TestOutcome.Failed, new InvalidOperationException("sform exited with 3."));

        report.Outcome.ShouldBe(TestOutcome.Failed);
        report.Artifacts.ShouldBeEmpty();
        report.Properties.ShouldNotContainKey(TestRunProperties.Provider);
        report.Properties.ShouldNotContainKey(TestRunProperties.Engine);
        report.Properties[TestRunProperties.User].ShouldBe(scope.User.Fingerprint);
        files.Directory.Exists(scope.Artifacts.Directory).ShouldBeFalse();
        session.Users.LeasedCount.ShouldBe(0);
        provider.Released.ShouldBeEmpty();
        factoryCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Create_TakesATestWithoutAUser_WithoutABrowser()
    {
        await using var session = await StartAsync();

        var scope = TestScope.Create(session, TestData.Identity("Anonymous"), new TestScopeOptions { Browser = false, LeaseUser = false });
        await scope.BeginAsync();

        scope.HasUser.ShouldBeFalse();
        session.Users.LeasedCount.ShouldBe(0);
        Should.Throw<ArgumentException>(() => TestScope.Create(session, TestData.Identity("Page"), new TestScopeOptions { LeaseUser = false }));
        await scope.EndAsync(TestOutcome.Passed);
    }

    [Fact]
    public async Task AttachTextAsync_ListsARedactedFile_InTheReport()
    {
        await using var session = await StartAsync();
        var scope = TestScope.Create(session, TestData.Identity("Command"), new TestScopeOptions { Browser = false });
        await scope.BeginAsync();

        var artifact = await scope.AttachTextAsync("sform-1.txt", "sform account as ada@example.test with pw-1\nexit code: 0\n");
        var report = await scope.EndAsync(TestOutcome.Passed);

        artifact.Kind.ShouldBe(ArtifactKind.Output);
        report.Artifacts.ShouldHaveSingleItem().Name.ShouldBe("sform-1.txt");
        files.File.ReadAllText(artifact.Path).ShouldBe("sform account as *** with ***\nexit code: 0\n");
    }

    [Fact]
    public async Task AddSecret_HidesAValueOfTheRun_InAttachmentsTheConsoleLogAndTheReport()
    {
        const string apiKey = "sk/run+9f8e7d6c";
        await using var session = await StartAsync();
        var scope = TestScope.Create(session, TestData.Identity("MachineCreate"), new TestScopeOptions { Browser = false });
        await scope.BeginAsync();
        await scope.OpenBrowserAsync();
        RaiseConsole(contexts.ShouldHaveSingleItem(), $"key {apiKey}");

        scope.AddSecret(apiKey);
        scope.AddSecret("ab");
        var artifact = await scope.AttachTextAsync("sform-1.txt", $"client secret: {apiKey}\nencoded: {Uri.EscapeDataString(apiKey)}\nab\n");
        var report = await scope.EndAsync(TestOutcome.Failed, new InvalidOperationException($"sform printed {apiKey} for pw-1"), output: $"{{\"apiKey\":\"{apiKey}\"}}");

        files.File.ReadAllText(artifact.Path).ShouldBe("client secret: ***\nencoded: ***\nab\n");
        var failure = report.Failure.ShouldNotBeNull();
        failure.Message.ShouldBe("sform printed *** for ***");
        failure.StackTrace.ShouldNotBeNull().ShouldNotContain(apiKey);
        report.Output.ShouldBe("{\"apiKey\":\"***\"}");
        var consoleLog = report.Artifacts.Single(current => current.Kind == ArtifactKind.ConsoleLog);
        files.File.ReadAllText(consoleLog.Path).ShouldEndWith("[log] key ***\n");
        scope.Redact($"ada@example.test {apiKey}").ShouldBe("*** ***");
    }

    [Fact]
    public async Task OpenBrowserAsync_ClosesAContext_ThatOpenedAfterTheScopeEnded()
    {
        var opening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await StartAsync();
        var scope = TestScope.Create(session, TestData.Identity("DeviceCode"), new TestScopeOptions { Browser = false });
        await scope.BeginAsync();
        HoldContext = async () =>
        {
            opening.TrySetResult();
            await release.Task;
        };

        var open = scope.OpenBrowserAsync();
        await opening.Task;
        var report = await scope.EndAsync(TestOutcome.Failed, new TimeoutException("The test timed out."));
        release.SetResult();

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => open);
        exception.Message.ShouldBe("The test scope has ended (EndAsync); it opens no more browser contexts.");
        contexts.ShouldHaveSingleItem();
        Volatile.Read(ref contextsClosed).ShouldBe(1);
        provider.Released.ShouldHaveSingleItem().ShouldBeSameAs(report);
    }

    [Fact]
    public async Task OpenBrowserAsync_AcquiresTheBrowserOnce_AndClosesItsSignedInContexts_AtTheEnd()
    {
        await using var session = await StartAsync();
        var scope = TestScope.Create(session, TestData.Identity("DeviceCode"), new TestScopeOptions { Browser = false });
        await scope.BeginAsync();

        var signedIn = await scope.OpenBrowserAsync(scope.User);
        var anonymous = await scope.OpenBrowserAsync();

        factoryCalls.ShouldBe(1);
        provider.Acquired.ShouldBe([scope.Test]);
        signedIn.ShouldNotBeSameAs(anonymous);
        startedFrom.ShouldBe([null, State("R1"), null]);
        flow.SignIns.ShouldBe(1);
        scope.Browser.Browser.ShouldBeSameAs(browser.Object);
        Should.Throw<InvalidOperationException>(() => scope.Page);

        state = State("R2");
        var report = await scope.EndAsync(TestOutcome.Passed);

        Volatile.Read(ref contextsClosed).ShouldBe(3);
        provider.Released.ShouldHaveSingleItem().ShouldBeSameAs(report);
        session.StorageStates.ShouldNotBeNull().TryGet(scope.User, out var cached).ShouldBeTrue();
        cached.ShouldNotBeNull().ShouldContain("\"R2\"");
        report.Properties[TestRunProperties.Provider].ShouldBe("Local");
        report.Artifacts.ShouldBeEmpty();
        session.Users.LeasedCount.ShouldBe(0);
        await Should.ThrowAsync<InvalidOperationException>(() => scope.OpenBrowserAsync());
    }

    [Fact]
    public async Task OpenBrowserAsync_UsesTheBrowser_OfAScopeWithOne()
    {
        await using var session = await StartAsync();
        var scope = TestScope.Create(session, TestData.Identity("Page"));
        await scope.BeginAsync();

        await scope.OpenBrowserAsync();

        provider.Acquired.ShouldBe([scope.Test]);
        await scope.EndAsync(TestOutcome.Passed);
        provider.Released.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task OpenBrowserAsync_NeedsTheScopeToHaveBegun()
    {
        await using var session = await StartAsync();
        var scope = TestScope.Create(session, TestData.Identity("Early"), new TestScopeOptions { Browser = false });

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => scope.OpenBrowserAsync());

        exception.Message.ShouldBe("The test scope has not begun (BeginAsync) or its start failed.");
        factoryCalls.ShouldBe(0);
        await scope.DisposeAsync();
    }

    [Fact]
    public async Task OpenBrowserAsync_FailsWithTheMessageOfAProviderThatDidNotStart_AndTheStartIsNotTriedAgain()
    {
        Factory = () => throw new BrowserUnavailableException("The chromium browser did not launch; install it with 'pwsh bin/<configuration>/net10.0/playwright.ps1 install chromium'.");
        await using var session = await StartAsync();
        var first = TestScope.Create(session, TestData.Identity("First"), new TestScopeOptions { Browser = false });
        var second = TestScope.Create(session, TestData.Identity("Second"));
        await first.BeginAsync();

        var opened = await Should.ThrowAsync<BrowserUnavailableException>(() => first.OpenBrowserAsync());
        var begun = await Should.ThrowAsync<BrowserUnavailableException>(() => second.BeginAsync());
        var report = await second.EndAsync(TestOutcome.Failed);

        opened.Message.ShouldStartWith("The chromium browser did not launch");
        begun.Message.ShouldBe(opened.Message);
        begun.ShouldNotBeSameAs(opened);
        report.Outcome.ShouldBe(TestOutcome.Blocked);
        factoryCalls.ShouldBe(1);
        await first.EndAsync(TestOutcome.Failed, opened);
        session.Users.LeasedCount.ShouldBe(0);
    }

    private static void RaiseConsole(Mock<IBrowserContext> context, string text)
    {
        var message = new Mock<IConsoleMessage>();
        message.SetupGet(current => current.Type).Returns("log");
        message.SetupGet(current => current.Text).Returns(text);
        message.SetupGet(current => current.Location).Returns(string.Empty);
        context.Raise(current => current.Console += null, context.Object, message.Object);
    }

    private static string State(string refreshToken)
    {
        return $$"""
            { "cookies": [], "origins": [ { "origin": "https://auth.example.test", "localStorage": [ { "name": "workos:refresh-token:client", "value": "{{refreshToken}}" } ] } ] }
            """;
    }

    private Task<AutomationSession> StartAsync()
    {
        files.AddFile(files.Path.Combine(BaseDirectory, "testsettings.json"), new MockFileData(Settings));
        var settings = UiAutomationSettingsLoader.Load(new UiAutomationSettingsOptions
        {
            BaseDirectory = BaseDirectory,
            FileSystem = files,
            EnvironmentVariables = new Dictionary<string, string?>(),
        });

        return AutomationSession.StartAsync(settings, new AutomationSessionOptions
        {
            SignInFlows = [flow],
            FileSystem = files,
            TimeProvider = time,
            Logger = logger,
            RunId = "20261108-101500-abcd",
            Environment = new RunEnvironment(null, null, null, null, "runner"),
            BrowserProviderStart = BrowserProviderStart.OnFirstLease,
            BrowserProviderFactory = (_, _) =>
            {
                Interlocked.Increment(ref factoryCalls);
                return Factory?.Invoke() ?? Task.FromResult<IBrowserProvider>(provider);
            },
        });
    }

    private Mock<IBrowserContext> NewContext()
    {
        var pages = new List<IPage>();
        var context = new Mock<IBrowserContext>();
        context.SetupGet(current => current.Tracing).Returns(Mock.Of<ITracing>());
        context.SetupGet(current => current.Pages).Returns(() => [.. pages]);
        context.Setup(current => current.NewPageAsync()).ReturnsAsync(() =>
        {
            var page = new Mock<IPage>();
            page.SetupGet(current => current.Url).Returns("https://auth.example.test/device");
            pages.Add(page.Object);
            return page.Object;
        });
        context.Setup(current => current.StorageStateAsync(It.IsAny<BrowserContextStorageStateOptions?>())).ReturnsAsync(() => state);
        context.Setup(current => current.CloseAsync(It.IsAny<BrowserContextCloseOptions?>())).Returns(() =>
        {
            Interlocked.Increment(ref contextsClosed);
            return Task.CompletedTask;
        });

        lock (contexts)
        {
            contexts.Add(context);
        }

        return context;
    }
}
