using System.IO.Abstractions.TestingHelpers;
using _42.Testing.UI.Automation.Runtime.Browsers;
using _42.Testing.UI.Automation.Runtime.Reporting;
using _42.Testing.UI.Automation.Runtime.Settings;
using _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Playwright;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Browsers;

// BrowserProviderStart.OnFirstLease: the session starts its provider only when a scope asks for a browser, so a run of
// tests without a browser never starts Playwright; WithSession, the default, starts it with the session as before.
public sealed class LazyBrowserProviderTests
{
    private const string Settings = """
        {
          "Target": { "BaseUrl": "https://api.example.test/api", "SignIn": "None" },
          "Execution": { "Trace": "Off" },
          "Users": { "Pool": [ { "Email": "ada@example.test" } ] }
        }
        """;

    private static readonly string BaseDirectory = MockUnixSupport.Path(@"C:\suite\bin");

    private readonly MockFileSystem files = new();
    private readonly FakeTimeProvider time = new(TestData.Start);
    private readonly FakeBrowserProvider provider = new() { Browser = new FakeRemoteBrowser().Object };
    private int factoryCalls;

    [Fact]
    public async Task StartAsync_StartsNoProvider_OnFirstLease_AndStopsWithoutOne()
    {
        var session = await StartAsync(Load(Settings), BrowserProviderStart.OnFirstLease);
        var scope = TestScope.Create(session, TestData.Identity("Command"), new TestScopeOptions { Browser = false });
        await scope.BeginAsync();
        await scope.EndAsync(TestOutcome.Passed);

        await session.StopAsync();

        factoryCalls.ShouldBe(0);
        session.BrowserProvider.Kind.ShouldBe(BrowserProviderKind.Local);
        session.Run.Properties.ShouldNotContainKey(TestRunProperties.BrowserVersion);
        provider.Disposed.ShouldBeFalse();
    }

    [Fact]
    public async Task StartAsync_StartsTheProvider_WithTheSession_ByDefault()
    {
        await using var session = await StartAsync(Load(Settings), BrowserProviderStart.WithSession);

        factoryCalls.ShouldBe(1);
        session.BrowserProvider.ShouldBeSameAs(provider);
    }

    [Fact]
    public async Task StartAsync_StartsTheProvider_WithTheSession_ForABrowserDevice_WhoseDescriptorComesFromTheDriver()
    {
        var devices = new DeviceCatalog(new Dictionary<string, BrowserNewContextOptions> { ["Pixel 7"] = new() { IsMobile = true, HasTouch = true } });
        var withDevice = new FakeBrowserProvider(devices) { Browser = new FakeRemoteBrowser().Object };
        var settings = Load("""
            {
              "Target": { "BaseUrl": "https://admin.example.test", "SignIn": "None" },
              "Browser": { "Device": "Pixel 7" },
              "Users": { "Pool": [ { "Email": "ada@example.test" } ] }
            }
            """);

        await using var session = await StartAsync(settings, BrowserProviderStart.OnFirstLease, withDevice);

        factoryCalls.ShouldBe(1);
        session.ContextOptions.Device.ShouldBe("Pixel 7");
    }

    [Fact]
    public async Task TheFirstLease_StartsTheProviderOnce_ForScopesThatAskAtTheSameTime()
    {
        var release = new TaskCompletionSource<IBrowserProvider>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lazy = Lazy(_ => release.Task);
        var test = TestData.Identity("Page");

        var first = lazy.AcquireAsync(test, CancellationToken.None).AsTask();
        var second = lazy.AcquireAsync(test, CancellationToken.None).AsTask();
        await Eventually.TrueAsync(() => Volatile.Read(ref factoryCalls) == 1, "the provider is starting");
        lazy.IsStarted.ShouldBeFalse();
        release.SetResult(provider);
        await Task.WhenAll(first, second);

        factoryCalls.ShouldBe(1);
        lazy.IsStarted.ShouldBeTrue();
        provider.Acquired.Count.ShouldBe(2);
        lazy.Properties.ShouldBeSameAs(provider.Properties);
    }

    [Fact]
    public async Task ATestThatGivesUp_DoesNotCancelTheStartForTheOthers()
    {
        var release = new TaskCompletionSource<IBrowserProvider>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lazy = Lazy(_ => release.Task);
        using var cancellation = new CancellationTokenSource();

        var givingUp = lazy.AcquireAsync(TestData.Identity("Gives up"), cancellation.Token).AsTask();
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => givingUp);
        release.SetResult(provider);

        var lease = await lazy.AcquireAsync(TestData.Identity("Waits"), CancellationToken.None);

        lease.Browser.ShouldBeSameAs(provider.Browser);
        factoryCalls.ShouldBe(1);
    }

    [Fact]
    public async Task TestsCompletedBeforeTheStart_ReachTheStartedProvider()
    {
        var lazy = Lazy(_ => Task.FromResult<IBrowserProvider>(provider));
        var skipped = TestData.Identity("Skipped");

        lazy.TestCompleted(skipped);
        provider.Completed.ShouldBeEmpty();
        await lazy.AcquireAsync(TestData.Identity("Page"), CancellationToken.None);
        lazy.TestCompleted(TestData.Identity("Page"));

        provider.Completed.Select(test => test.MethodName).ShouldBe(["Skipped", "Page"]);
    }

    [Fact]
    public async Task DisposeAsync_DisposesTheStartedProvider_AndNeverStartsOne()
    {
        var unused = Lazy(_ => Task.FromResult<IBrowserProvider>(new FakeBrowserProvider()));
        await unused.DisposeAsync();
        factoryCalls.ShouldBe(0);

        var used = Lazy(_ => Task.FromResult<IBrowserProvider>(provider));
        await used.AcquireAsync(TestData.Identity("Page"), CancellationToken.None);
        await used.DisposeAsync();

        provider.Disposed.ShouldBeTrue();
        await Should.ThrowAsync<ObjectDisposedException>(() => used.AcquireAsync(TestData.Identity("Late"), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task AFailedStart_FailsEveryLease_AsBrowserUnavailable()
    {
        var lazy = Lazy(_ => Task.FromException<IBrowserProvider>(new InvalidOperationException("The Playwright driver did not start: node not found.")));

        var first = await Should.ThrowAsync<BrowserUnavailableException>(() => lazy.AcquireAsync(TestData.Identity("First"), CancellationToken.None).AsTask());
        var second = await Should.ThrowAsync<BrowserUnavailableException>(() => lazy.AcquireAsync(TestData.Identity("Second"), CancellationToken.None).AsTask());

        first.Message.ShouldBe("The Playwright driver did not start: node not found.");
        first.InnerException.ShouldBeOfType<InvalidOperationException>();
        second.ShouldNotBeSameAs(first);
        factoryCalls.ShouldBe(1);
        AutomationOutcomes.Refine(TestOutcome.Failed, second).ShouldBe(TestOutcome.Blocked);
    }

    private LazyBrowserProvider Lazy(Func<CancellationToken, Task<IBrowserProvider>> start)
    {
        return new LazyBrowserProvider(
            Load(Settings),
            token =>
            {
                Interlocked.Increment(ref factoryCalls);
                return start(token);
            },
            NullLogger.Instance);
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

    private Task<AutomationSession> StartAsync(UiAutomationSettings settings, BrowserProviderStart start, FakeBrowserProvider? started = null)
    {
        return AutomationSession.StartAsync(settings, new AutomationSessionOptions
        {
            FileSystem = files,
            TimeProvider = time,
            RunId = "20261108-101500-abcd",
            Environment = new RunEnvironment(null, null, null, null, "runner"),
            BrowserProviderStart = start,
            BrowserProviderFactory = (_, _) =>
            {
                Interlocked.Increment(ref factoryCalls);
                return Task.FromResult<IBrowserProvider>(started ?? provider);
            },
        });
    }
}
