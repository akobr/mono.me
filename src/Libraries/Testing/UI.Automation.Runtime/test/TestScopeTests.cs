using System.IO.Abstractions.TestingHelpers;
using _42.Testing.UI.Automation.Runtime.Browsers;
using _42.Testing.UI.Automation.Runtime.Reporting;
using _42.Testing.UI.Automation.Runtime.Settings;
using _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;
using _42.Testing.UI.Automation.Runtime.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Playwright;
using Moq;

namespace _42.Testing.UI.Automation.Runtime.UnitTests;

// The hand-off of a sign-in between the tests of a user: a scope starts signed in from the cached state, and at its end
// hands the context's refreshed state back to the cache, or drops it when the page shows the user signed out.
public sealed class TestScopeTests
{
    private const string Settings = """
        {
          "Target": { "BaseUrl": "https://admin.example.test", "SignIn": "AuthKit" },
          "Execution": { "Trace": "Off", "Screenshot": "Off" },
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

    // What StorageStateAsync of every context returns from now on; an exception makes it throw.
    private string state = State("R1");
    private Exception? stateFailure;

    // What the URL of every page is.
    private string pageUrl = "https://admin.example.test/orgs";

    public TestScopeTests()
    {
        browser.Setup(current => current.NewContextAsync(It.IsAny<BrowserNewContextOptions?>())).ReturnsAsync((BrowserNewContextOptions? options) =>
        {
            lock (startedFrom)
            {
                startedFrom.Add(options?.StorageState);
            }

            return NewContext().Object;
        });
    }

    [Fact]
    public async Task EndAsync_HandsTheRefreshedSignIn_ToTheUsersNextTest()
    {
        await using var session = await StartAsync();
        var scope = await BeginAsync(session);
        var user = scope.User;
        startedFrom.ShouldBe([null, State("R1")]);

        state = State("R2");
        await scope.EndAsync(TestOutcome.Passed);

        session.StorageStates.ShouldNotBeNull().TryGet(user, out var cached).ShouldBeTrue();
        cached.ShouldNotBeNull().ShouldContain("\"R2\"");
        var next = await BeginAsync(session);
        next.User.ShouldBe(user);
        startedFrom[^1].ShouldNotBeNull().ShouldContain("\"R2\"");
        flow.SignIns.ShouldBe(1);
        await next.EndAsync(TestOutcome.Passed);
    }

    [Fact]
    public async Task EndAsync_DropsTheSignIn_OfATestThatEndedSignedOut_SoTheNextTestSignsInAgain()
    {
        await using var session = await StartAsync();
        var scope = await BeginAsync(session);
        var user = scope.User;

        pageUrl = "https://admin.example.test/login";
        flow.SignedOut = page => page.Url.EndsWith("/login", StringComparison.Ordinal);
        await scope.EndAsync(TestOutcome.Failed, new InvalidOperationException("Signed out."));

        session.StorageStates.ShouldNotBeNull().TryGet(user, out _).ShouldBeFalse();
        pageUrl = "https://admin.example.test/orgs";
        var next = await BeginAsync(session);
        flow.SignIns.ShouldBe(2);
        await next.EndAsync(TestOutcome.Passed);
    }

    [Fact]
    public async Task EndAsync_DropsTheSignIn_WhenItsRefreshFails()
    {
        await using var session = await StartAsync();
        var scope = await BeginAsync(session);
        var user = scope.User;

        stateFailure = new PlaywrightException("Target page, context or browser has been closed");
        var report = await scope.EndAsync(TestOutcome.Passed);

        report.Outcome.ShouldBe(TestOutcome.Passed);
        session.StorageStates.ShouldNotBeNull().TryGet(user, out _).ShouldBeFalse();
        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Text.StartsWith("SignedIn: keeping the sign-in failed.", StringComparison.Ordinal));
        session.Users.LeasedCount.ShouldBe(0);
    }

    [Fact]
    public async Task EndAsync_HandsBackTheSignInOfAnExtraContext_AndReturnsEveryUser()
    {
        await using var session = await StartAsync();
        var scope = await BeginAsync(session);
        var other = await scope.LeaseAnotherUserAsync(UserRequirement.Any);
        await scope.NewContextAsync(other);
        flow.SignIns.ShouldBe(2);
        session.Users.LeasedCount.ShouldBe(2);

        state = State("R2");
        await scope.EndAsync(TestOutcome.Passed);

        var cache = session.StorageStates.ShouldNotBeNull();
        cache.TryGet(scope.User, out var mine).ShouldBeTrue();
        mine.ShouldNotBeNull().ShouldContain("\"R2\"");
        cache.TryGet(other, out var theirs).ShouldBeTrue();
        theirs.ShouldNotBeNull().ShouldContain("\"R2\"");
        session.Users.LeasedCount.ShouldBe(0);
    }

    [Fact]
    public async Task EndAsync_ReturnsTheUser_OfAScopeWhoseBrowserFailed()
    {
        await using var session = await StartAsync(new FakeBrowserProvider());
        var scope = TestScope.Create(session, TestData.Identity("NoBrowser"));

        await Should.ThrowAsync<NotSupportedException>(() => scope.BeginAsync());
        session.Users.LeasedCount.ShouldBe(1);
        var report = await scope.EndAsync(TestOutcome.Failed);

        scope.BeginException.ShouldBeOfType<NotSupportedException>();
        report.Failure.ShouldNotBeNull().ExceptionType.ShouldBe(typeof(NotSupportedException).FullName);
        session.Users.LeasedCount.ShouldBe(0);
        flow.SignIns.ShouldBe(0);
    }

    // TUnit's hook timeout ends the hook's token, not the test's: the start is Blocked by the step that did not finish.
    [Fact]
    public async Task BeginAsync_BlocksALeaseThatTheEnginesBudgetEnded()
    {
        await using var session = await StartAsync();
        var leased = new[] { await session.Users.LeaseAsync(UserRequirement.Any), await session.Users.LeaseAsync(UserRequirement.Any) };
        var scope = TestScope.Create(session, TestData.Identity("Waits"), new TestScopeOptions { TestCancellation = CancellationToken.None });
        using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var exception = await Should.ThrowAsync<UserLeaseException>(() => scope.BeginAsync(budget.Token));
        var report = await scope.EndAsync(TestOutcome.Failed);

        exception.Message.ShouldStartWith("No user with any role was leased before the engine's time for the start of the test ran out");
        scope.BeginException.ShouldBeSameAs(exception);
        report.Outcome.ShouldBe(TestOutcome.Blocked);
        session.Users.LeasedCount.ShouldBe(2);
        foreach (var user in leased)
        {
            session.Users.Return(user);
        }
    }

    [Fact]
    public async Task BeginAsync_BlocksABrowserThatTheEnginesBudgetEnded()
    {
        var provider = new FakeBrowserProvider { Browser = browser.Object, Hold = new TaskCompletionSource() };
        await using var session = await StartAsync(provider);
        var scope = TestScope.Create(session, TestData.Identity("Waits"), new TestScopeOptions { TestCancellation = CancellationToken.None });
        using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var exception = await Should.ThrowAsync<BrowserUnavailableException>(() => scope.BeginAsync(budget.Token));
        var report = await scope.EndAsync(TestOutcome.Failed);

        exception.Message.ShouldBe("No Local browser was available before the engine's time for the start of the test ran out (e.g. its hook timeout).");
        report.Outcome.ShouldBe(TestOutcome.Blocked);
        session.Users.LeasedCount.ShouldBe(0);
    }

    [Fact]
    public async Task BeginAsync_StaysCancelled_WhenTheTestItselfIsCancelled_OrNoTestCancellationIsGiven()
    {
        var provider = new FakeBrowserProvider { Browser = browser.Object, Hold = new TaskCompletionSource() };
        await using var session = await StartAsync(provider);
        using var stopped = new CancellationTokenSource();
        await stopped.CancelAsync();
        var cancelledTest = TestScope.Create(session, TestData.Identity("Stopped"), new TestScopeOptions { TestCancellation = stopped.Token });
        var withoutOne = TestScope.Create(session, TestData.Identity("Plain"));

        await Should.ThrowAsync<OperationCanceledException>(() => cancelledTest.BeginAsync(stopped.Token));
        using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Should.ThrowAsync<OperationCanceledException>(() => withoutOne.BeginAsync(budget.Token));

        (await cancelledTest.EndAsync(TestOutcome.Cancelled)).Outcome.ShouldBe(TestOutcome.Cancelled);
        (await withoutOne.EndAsync(TestOutcome.Cancelled)).Outcome.ShouldBe(TestOutcome.Cancelled);
        session.Users.LeasedCount.ShouldBe(0);
    }

    private static string State(string refreshToken)
    {
        return $$"""
            { "cookies": [], "origins": [ { "origin": "https://admin.example.test", "localStorage": [ { "name": "workos:refresh-token:client", "value": "{{refreshToken}}" } ] } ] }
            """;
    }

    private async Task<TestScope> BeginAsync(AutomationSession session)
    {
        var scope = TestScope.Create(session, TestData.Identity("SignedIn"));
        await scope.BeginAsync();
        return scope;
    }

    private Task<AutomationSession> StartAsync(FakeBrowserProvider? provider = null)
    {
        files.AddFile(files.Path.Combine(BaseDirectory, "testsettings.json"), new MockFileData(Settings));
        var settings = UiAutomationSettingsLoader.Load(new UiAutomationSettingsOptions
        {
            BaseDirectory = BaseDirectory,
            FileSystem = files,
            EnvironmentVariables = new Dictionary<string, string?>(),
        });

        provider ??= new FakeBrowserProvider { Browser = browser.Object };
        return AutomationSession.StartAsync(settings, new AutomationSessionOptions
        {
            SignInFlows = [flow],
            FileSystem = files,
            TimeProvider = time,
            Logger = logger,
            RunId = "20261031-142500-abcd",
            Environment = new RunEnvironment(null, null, null, null, "runner"),
            BrowserProviderFactory = (_, _) => Task.FromResult<IBrowserProvider>(provider),
        });
    }

    private Mock<IBrowserContext> NewContext()
    {
        var pages = new List<IPage>();
        var context = new Mock<IBrowserContext>();
        context.SetupGet(current => current.Pages).Returns(() => [.. pages]);
        context.Setup(current => current.NewPageAsync()).ReturnsAsync(() =>
        {
            var page = new Mock<IPage>();
            page.SetupGet(current => current.Url).Returns(() => pageUrl);
            pages.Add(page.Object);
            return page.Object;
        });
        context.Setup(current => current.StorageStateAsync(It.IsAny<BrowserContextStorageStateOptions?>()))
            .Returns(() => stateFailure is { } failure ? Task.FromException<string>(failure) : Task.FromResult(state));
        context.Setup(current => current.CloseAsync(It.IsAny<BrowserContextCloseOptions?>())).Returns(Task.CompletedTask);
        return context;
    }
}
