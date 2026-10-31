using System.Text.Json;
using _42.Testing.UI.Automation.Runtime.Browsers;
using _42.Testing.UI.Automation.Runtime.Reporting;
using _42.Testing.UI.Automation.Runtime.Settings;
using _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Playwright;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Browsers;

public sealed class BrowserStackBrowserProviderTests
{
    // Characters that every encoding of a URL or JSON document changes.
    private const string AccessKey = "bs+Key/42=secret value&\"x";

    private readonly FakeRemoteBrowserConnector connector = new();
    private readonly ListLogger logger = new();
    private readonly FakeTimeProvider time = new(TestData.Start);

    [Theory]
    [InlineData("chrome", BrowserEngine.Chromium)]
    [InlineData("edge", BrowserEngine.Chromium)]
    [InlineData("playwright-chromium", BrowserEngine.Chromium)]
    [InlineData("playwright-firefox", BrowserEngine.Firefox)]
    [InlineData("playwright-webkit", BrowserEngine.Webkit)]
    public async Task AcquireAsync_ConnectsWithTheEngineOfTheBrowser(string browser, BrowserEngine engine)
    {
        await using var provider = Provider(settings => settings.Browser = browser);

        await provider.AcquireAsync(Test("SignInTests", "SignIn"), CancellationToken.None);

        connector.Connections.ShouldHaveSingleItem().Engine.ShouldBe(engine);
        provider.Properties[TestRunProperties.Engine].ShouldBe(engine.ToString().ToLowerInvariant());
        provider.Properties[TestRunProperties.Channel].ShouldBe(browser);
    }

    [Fact]
    public async Task AcquireAsync_ConnectsASessionPerTest_NamedAfterIt()
    {
        await using var provider = Provider(settings => settings.Endpoint = "ws://localhost:4242/", slowMoMs: 50);

        var first = await provider.AcquireAsync(Test("SignInTests", "SignIn_ShowsOrganizations"), CancellationToken.None);
        var second = await provider.AcquireAsync(Test("ProjectsTests", "Project_Create_OpensSettings"), CancellationToken.None);

        connector.Connections.Count.ShouldBe(2);
        first.Browser.ShouldNotBeSameAs(second.Browser);
        first.RemoteSessionName.ShouldBe("SignInTests.SignIn_ShowsOrganizations");
        second.RemoteSessionName.ShouldBe("ProjectsTests.Project_Create_OpensSettings");
        first.Properties[TestRunProperties.BrowserVersion].ShouldBe("142.0.7444.10");
        first.Properties[TestRunProperties.Os].ShouldBe("Windows 11");
        first.Properties[TestRunProperties.Provider].ShouldBe("BrowserStack");

        var connection = connector.Connections[0];
        connection.Endpoint.ShouldStartWith("ws://localhost:4242/?caps=");
        connection.Options.Timeout.ShouldBe(300_000f);
        connection.Options.SlowMo.ShouldBe(50f);
        var caps = Caps(connection.Endpoint);
        caps["name"].ShouldBe("SignInTests.SignIn_ShowsOrganizations");
        caps["build"].ShouldBe("local BUILDBOX 2026-10-31");
        caps["browserstack.username"].ShouldBe("ada");
        caps["browserstack.accessKey"].ShouldBe(AccessKey);
        caps["client.playwrightVersion"].ShouldBe(AutomationSession.PlaywrightVersion);
        caps.ShouldNotContainKey("browserstack.playwrightVersion");
    }

    [Fact]
    public void Provider_ExposesTheSessionLimit_AndWhatTheRunShows()
    {
        var provider = Provider(settings => settings.MaxParallelSessions = 3);

        provider.Kind.ShouldBe(BrowserProviderKind.BrowserStack);
        provider.MaxParallelSessions.ShouldBe(3);
        provider.Build.ShouldBe("local BUILDBOX 2026-10-31");
        provider.Properties.ShouldBe(
            new Dictionary<string, string>
            {
                [TestRunProperties.Provider] = "BrowserStack",
                [TestRunProperties.Engine] = "chromium",
                [TestRunProperties.Channel] = "chrome",
                [TestRunProperties.Os] = "Windows 11",
                [TestRunProperties.BrowserVersion] = "latest",
            },
            ignoreOrder: true);
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReleaseAsync_MarksAPassedTestPassed_OnANewPage_AndClosesItsSession()
    {
        await using var provider = Provider();
        var test = Test("SignInTests", "SignIn");
        var lease = await provider.AcquireAsync(test, CancellationToken.None);
        var browser = connector.Connections.ShouldHaveSingleItem().Browser;

        await provider.ReleaseAsync(lease, Report(test, TestOutcome.Passed), CancellationToken.None);

        browser.ContextsCreated.ShouldBe(1);
        var (expression, argument) = browser.Evaluated.ShouldHaveSingleItem();
        expression.ShouldBe("_ => {}");
        argument.ShouldBe("browserstack_executor: {\"action\":\"setSessionStatus\",\"arguments\":{\"status\":\"passed\"}}");
        browser.Closes.ShouldBe(1);
    }

    [Fact]
    public async Task ReleaseAsync_MarksAFailedTestFailed_WithItsMessage()
    {
        await using var provider = Provider();
        var test = Test("SignInTests", "SignIn");
        var lease = await provider.AcquireAsync(test, CancellationToken.None);
        var browser = connector.Connections.ShouldHaveSingleItem().Browser;

        await provider.ReleaseAsync(lease, Report(test, TestOutcome.Failed, "Expected \"acme\"\nbut it was hidden"), CancellationToken.None);

        var status = Status(browser.Evaluated.ShouldHaveSingleItem().Argument);
        status.GetProperty("status").GetString().ShouldBe("failed");
        status.GetProperty("reason").GetString().ShouldBe("Expected \"acme\" but it was hidden");
        browser.Closes.ShouldBe(1);
    }

    [Fact]
    public async Task ReleaseAsync_LeavesASkippedTestUnmarked_AndClosesItsSession()
    {
        await using var provider = Provider();
        var test = Test("SignInTests", "SignIn");
        var lease = await provider.AcquireAsync(test, CancellationToken.None);
        var browser = connector.Connections.ShouldHaveSingleItem().Browser;

        await provider.ReleaseAsync(lease, Report(test, TestOutcome.Skipped), CancellationToken.None);

        browser.Evaluated.ShouldBeEmpty();
        browser.Closes.ShouldBe(1);
    }

    [Fact]
    public async Task AcquireAsync_HidesTheAccessKey_InTheErrorOfAConnectionThatFailed()
    {
        connector.Fail = endpoint => new PlaywrightException(
            $"WebSocket error: wss://cdp.browserstack.com/playwright 401 Unauthorized\nCall log:\n  - <ws connecting> {endpoint}\n  - <ws error> {AccessKey} rejected");
        await using var provider = Provider();

        var exception = await Should.ThrowAsync<BrowserUnavailableException>(async () => await provider.AcquireAsync(Test("SignInTests", "SignIn"), CancellationToken.None));

        exception.InnerException.ShouldBeNull();
        exception.Message.ShouldStartWith("The BrowserStack session 'SignInTests.SignIn' did not start at wss://cdp.browserstack.com/playwright: PlaywrightException: WebSocket error:");
        exception.Message.ShouldContain("***");
        ShouldHideTheAccessKey(exception.ToString());
    }

    [Fact]
    public async Task ReleaseAsync_HidesTheAccessKey_InTheWarningOfAMarkThatFailed_AndStillCloses()
    {
        await using var provider = Provider();
        var test = Test("SignInTests", "SignIn");
        var lease = await provider.AcquireAsync(test, CancellationToken.None);
        var browser = connector.Connections.ShouldHaveSingleItem().Browser;
        browser.FailEvaluate = new PlaywrightException($"Target closed; endpoint {connector.Connections[0].Endpoint}");

        await provider.ReleaseAsync(lease, Report(test, TestOutcome.Failed, $"the key {AccessKey} leaked into a message"), CancellationToken.None);

        var warning = logger.Entries.ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Text.ShouldStartWith("The BrowserStack session SignInTests.SignIn was not marked failed: PlaywrightException: Target closed;");
        ShouldHideTheAccessKey(warning.Text);
        Status(browser.Evaluated.ShouldHaveSingleItem().Argument).GetProperty("reason").GetString().ShouldBe("the key *** leaked into a message");
        browser.Closes.ShouldBe(1);
    }

    [Fact]
    public async Task SessionPerClass_SharesASessionPerClass_AndClosesItAfterTheLastPlannedTest()
    {
        var first = Test("AnnotationsTests", "Create");
        var second = Test("AnnotationsTests", "Delete");
        var other = Test("ProjectsTests", "Create");
        await using var provider = Provider(settings => settings.SessionPerTest = false, planned: [first, second, other]);

        var firstLease = await provider.AcquireAsync(first, CancellationToken.None);
        var secondLease = await provider.AcquireAsync(second, CancellationToken.None);
        var otherLease = await provider.AcquireAsync(other, CancellationToken.None);

        connector.Connections.Count.ShouldBe(2);
        firstLease.Browser.ShouldBeSameAs(secondLease.Browser);
        otherLease.Browser.ShouldNotBeSameAs(firstLease.Browser);
        firstLease.RemoteSessionName.ShouldBe("AnnotationsTests");
        otherLease.RemoteSessionName.ShouldBe("ProjectsTests");
        Caps(connector.Connections.Single(connection => connection.Browser.Object == firstLease.Browser).Endpoint)["name"].ShouldBe("AnnotationsTests");

        var annotations = connector.Connections.Single(connection => connection.Browser.Object == firstLease.Browser).Browser;
        await provider.ReleaseAsync(firstLease, Report(first, TestOutcome.Failed, "Boom"), CancellationToken.None);
        annotations.Closes.ShouldBe(0);

        await provider.ReleaseAsync(secondLease, Report(second, TestOutcome.Passed), CancellationToken.None);
        annotations.Closes.ShouldBe(1);
        var status = Status(annotations.Evaluated.ShouldHaveSingleItem().Argument);
        status.GetProperty("status").GetString().ShouldBe("failed");
        status.GetProperty("reason").GetString().ShouldBe("1 of 2 test(s) failed; AnnotationsTests.Create: Boom");

        var projects = connector.Connections.Single(connection => connection.Browser.Object == otherLease.Browser).Browser;
        await provider.ReleaseAsync(otherLease, Report(other, TestOutcome.Passed), CancellationToken.None);
        projects.Closes.ShouldBe(1);
        Status(projects.Evaluated.ShouldHaveSingleItem().Argument).GetProperty("status").GetString().ShouldBe("passed");
    }

    [Fact]
    public async Task SessionPerClass_MarksATestByItsLastAttempt()
    {
        var first = Test("AnnotationsTests", "Create");
        var second = Test("AnnotationsTests", "Delete");
        await using var provider = Provider(settings => settings.SessionPerTest = false, planned: [first, second]);

        var secondLease = await provider.AcquireAsync(second, CancellationToken.None);
        var attempt1 = await provider.AcquireAsync(first, CancellationToken.None);
        await provider.ReleaseAsync(attempt1, Report(first, TestOutcome.Failed, "Flaky"), CancellationToken.None);
        var attempt2 = await provider.AcquireAsync(first, CancellationToken.None);
        await provider.ReleaseAsync(attempt2, Report(first, TestOutcome.Passed) with { Attempt = 2 }, CancellationToken.None);
        await provider.ReleaseAsync(secondLease, Report(second, TestOutcome.Passed), CancellationToken.None);

        var browser = connector.Connections.ShouldHaveSingleItem().Browser;
        browser.Closes.ShouldBe(1);
        Status(browser.Evaluated.ShouldHaveSingleItem().Argument).GetProperty("status").GetString().ShouldBe("passed");
    }

    [Fact]
    public async Task SessionPerClass_ConnectsOnce_ForTestsThatStartTogether()
    {
        var first = Test("AnnotationsTests", "Create");
        var second = Test("AnnotationsTests", "Delete");
        connector.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var provider = Provider(settings => settings.SessionPerTest = false, planned: [first, second]);

        var acquiring = new[] { provider.AcquireAsync(first, CancellationToken.None).AsTask(), provider.AcquireAsync(second, CancellationToken.None).AsTask() };
        await Eventually.SettleAsync();
        connector.Hold.SetResult();
        var leases = await Task.WhenAll(acquiring);

        connector.Connections.ShouldHaveSingleItem();
        leases[0].Browser.ShouldBeSameAs(leases[1].Browser);
    }

    [Fact]
    public async Task SessionPerClass_KeepsASessionOfAClassWithoutPlannedTests_UntilDisposed()
    {
        var test = Test("AnnotationsTests", "Create");
        var provider = Provider(settings => settings.SessionPerTest = false);

        var lease = await provider.AcquireAsync(test, CancellationToken.None);
        await provider.ReleaseAsync(lease, Report(test, TestOutcome.Failed, "Boom"), CancellationToken.None);
        var browser = connector.Connections.ShouldHaveSingleItem().Browser;
        browser.Closes.ShouldBe(0);

        await provider.DisposeAsync();

        browser.Closes.ShouldBe(1);
        Status(browser.Evaluated.ShouldHaveSingleItem().Argument).GetProperty("status").GetString().ShouldBe("failed");
        connector.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task SessionPerClass_ReplacesASessionThatEnded()
    {
        var first = Test("AnnotationsTests", "Create");
        var second = Test("AnnotationsTests", "Delete");
        await using var provider = Provider(settings => settings.SessionPerTest = false, planned: [first, second]);

        var firstLease = await provider.AcquireAsync(first, CancellationToken.None);
        await provider.ReleaseAsync(firstLease, Report(first, TestOutcome.Passed), CancellationToken.None);
        connector.Connections[0].Browser.Disconnect();
        var secondLease = await provider.AcquireAsync(second, CancellationToken.None);

        connector.Connections.Count.ShouldBe(2);
        secondLease.Browser.ShouldBeSameAs(connector.Connections[1].Browser.Object);
        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Text.Contains("AnnotationsTests has ended", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SessionPerClass_ConnectsAgain_AfterAConnectionThatFailed()
    {
        var first = Test("AnnotationsTests", "Create");
        var second = Test("AnnotationsTests", "Delete");
        var failures = 1;
        connector.Fail = _ => Interlocked.Decrement(ref failures) >= 0 ? new PlaywrightException("queue full") : null;
        await using var provider = Provider(settings => settings.SessionPerTest = false, planned: [first, second]);

        await Should.ThrowAsync<BrowserUnavailableException>(async () => await provider.AcquireAsync(first, CancellationToken.None));
        var lease = await provider.AcquireAsync(second, CancellationToken.None);

        connector.Connections.ShouldHaveSingleItem().Browser.Object.ShouldBeSameAs(lease.Browser);
    }

    [Fact]
    public async Task SessionPerClass_ClosesTheSession_WhenTheRestOfTheClassCompletesWithoutABrowser()
    {
        var first = Test("AnnotationsTests", "Create");
        var skipped = Test("AnnotationsTests", "Create_OnWebkit");
        var blocked = Test("AnnotationsTests", "Delete");
        await using var provider = Provider(settings => settings.SessionPerTest = false, planned: [first, skipped, blocked]);

        provider.TestCompleted(skipped);
        var lease = await provider.AcquireAsync(first, CancellationToken.None);
        await provider.ReleaseAsync(lease, Report(first, TestOutcome.Passed), CancellationToken.None);
        provider.TestCompleted(first);
        var browser = connector.Connections.ShouldHaveSingleItem().Browser;
        browser.Closes.ShouldBe(0);

        provider.TestCompleted(blocked);

        await Eventually.TrueAsync(() => browser.Closes == 1, "the session closed");
        Status(browser.Evaluated.ShouldHaveSingleItem().Argument).GetProperty("status").GetString().ShouldBe("passed");
    }

    [Fact]
    public async Task SessionPerClass_ClosesAnIdleSession_BeforeBrowserStacksIdleTimeout_AndALaterTestOpensAnother()
    {
        var first = Test("ProjectsTests", "Create");
        var serial = Test("ProjectsTests", "Settings_Change");
        await using var provider = Provider(settings => settings.SessionPerTest = false, planned: [first, serial]);

        var lease = await provider.AcquireAsync(first, CancellationToken.None);
        await provider.ReleaseAsync(lease, Report(first, TestOutcome.Failed, "Boom"), CancellationToken.None);
        var idle = connector.Connections.ShouldHaveSingleItem().Browser;

        provider.IdleCloseAfter.ShouldBe(TimeSpan.FromSeconds(60));
        time.Advance(TimeSpan.FromSeconds(59));
        await Eventually.SettleAsync();
        idle.Closes.ShouldBe(0);

        time.Advance(TimeSpan.FromSeconds(1));
        await Eventually.TrueAsync(() => idle.Closes == 1, "the idle session closed");
        Status(idle.Evaluated.ShouldHaveSingleItem().Argument).GetProperty("reason").GetString().ShouldBe("1 of 1 test(s) failed; ProjectsTests.Create: Boom");
        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Information && entry.Text.StartsWith("The BrowserStack session ProjectsTests was idle for 60 s", StringComparison.Ordinal));

        var later = await provider.AcquireAsync(serial, CancellationToken.None);
        connector.Connections.Count.ShouldBe(2);
        later.Browser.ShouldBeSameAs(connector.Connections[1].Browser.Object);
        await provider.ReleaseAsync(later, Report(serial, TestOutcome.Passed), CancellationToken.None);
        connector.Connections[1].Browser.Closes.ShouldBe(1);
        Status(connector.Connections[1].Browser.Evaluated.ShouldHaveSingleItem().Argument).GetProperty("status").GetString().ShouldBe("passed");
    }

    [Fact]
    public async Task SessionPerClass_KeepsAnIdleSession_ForATestThatJoinsBeforeItCloses()
    {
        var first = Test("ProjectsTests", "Create");
        var second = Test("ProjectsTests", "Delete");
        await using var provider = Provider(settings => settings.SessionPerTest = false, planned: [first, second]);

        await provider.ReleaseAsync(await provider.AcquireAsync(first, CancellationToken.None), Report(first, TestOutcome.Passed), CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(30));
        var lease = await provider.AcquireAsync(second, CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(60));
        await Eventually.SettleAsync();

        var browser = connector.Connections.ShouldHaveSingleItem().Browser;
        lease.Browser.ShouldBeSameAs(browser.Object);
        browser.Closes.ShouldBe(0);

        await provider.ReleaseAsync(lease, Report(second, TestOutcome.Passed), CancellationToken.None);
        browser.Closes.ShouldBe(1);
    }

    [Fact]
    public async Task SessionPerClass_IdleClose_FollowsTheIdleTimeoutCapability()
    {
        await using var provider = Provider(settings =>
        {
            settings.SessionPerTest = false;
            settings.AdditionalCapabilities["browserstack.idleTimeout"] = "300";
        });

        provider.IdleCloseAfter.ShouldBe(TimeSpan.FromSeconds(200));
    }

    [Fact]
    public async Task SessionPerClass_ClosesTheReplacement_WhenTheClassCompletesOnTheSessionItReplaced()
    {
        var first = Test("AnnotationsTests", "Create");
        var second = Test("AnnotationsTests", "Delete");
        var third = Test("AnnotationsTests", "Rename");
        await using var provider = Provider(settings => settings.SessionPerTest = false, planned: [first, second, third]);

        var firstLease = await provider.AcquireAsync(first, CancellationToken.None);
        var secondLease = await provider.AcquireAsync(second, CancellationToken.None);
        var ended = connector.Connections.ShouldHaveSingleItem().Browser;
        ended.Disconnect();
        var thirdLease = await provider.AcquireAsync(third, CancellationToken.None);
        var replacement = connector.Connections[1].Browser;
        thirdLease.Browser.ShouldBeSameAs(replacement.Object);

        await provider.ReleaseAsync(thirdLease, Report(third, TestOutcome.Failed, "Gone"), CancellationToken.None);
        replacement.Closes.ShouldBe(0);
        await provider.ReleaseAsync(firstLease, Report(first, TestOutcome.Passed), CancellationToken.None);
        ended.Closes.ShouldBe(0);
        await provider.ReleaseAsync(secondLease, Report(second, TestOutcome.Passed), CancellationToken.None);

        ended.Closes.ShouldBe(1);
        ended.Evaluated.ShouldBeEmpty();
        replacement.Closes.ShouldBe(1);
        var status = Status(replacement.Evaluated.ShouldHaveSingleItem().Argument);
        status.GetProperty("status").GetString().ShouldBe("failed");
        status.GetProperty("reason").GetString().ShouldBe("1 of 1 test(s) failed; AnnotationsTests.Rename: Gone");
    }

    [Fact]
    public async Task SessionPerClass_ClosesAReplacedSession_WhenItsLastTestLeavesIt()
    {
        var first = Test("AnnotationsTests", "Create");
        var second = Test("AnnotationsTests", "Delete");
        var third = Test("AnnotationsTests", "Rename");
        await using var provider = Provider(settings => settings.SessionPerTest = false, planned: [first, second, third]);

        var firstLease = await provider.AcquireAsync(first, CancellationToken.None);
        var ended = connector.Connections.ShouldHaveSingleItem().Browser;
        ended.Disconnect();
        var secondLease = await provider.AcquireAsync(second, CancellationToken.None);
        await provider.ReleaseAsync(firstLease, Report(first, TestOutcome.Failed, "Gone"), CancellationToken.None);

        ended.Closes.ShouldBe(1);
        connector.Connections[1].Browser.Closes.ShouldBe(0);
        secondLease.Browser.IsConnected.ShouldBeTrue();
    }

    [Fact]
    public async Task TestCompleted_ChangesNothing_ForASessionPerTest()
    {
        await using var provider = Provider();
        var test = Test("SignInTests", "SignIn");
        var lease = await provider.AcquireAsync(test, CancellationToken.None);

        provider.TestCompleted(test);
        await Eventually.SettleAsync();

        connector.Connections.ShouldHaveSingleItem().Browser.Closes.ShouldBe(0);
        await provider.ReleaseAsync(lease, Report(test, TestOutcome.Passed), CancellationToken.None);
        connector.Connections[0].Browser.Closes.ShouldBe(1);
    }

    [Fact]
    public async Task AcquireAsync_ExplainsAPlaywrightVersionThatTheServerDoesNotRun()
    {
        connector.Fail = _ => new PlaywrightException(
            "WebSocket error: wss://cdp.browserstack.com/playwright 428 Precondition Required\nPlaywright version mismatch:\n  - server version: v1.62\n  - client version: v1.63");
        await using var provider = Provider();

        var exception = await Should.ThrowAsync<BrowserUnavailableException>(async () => await provider.AcquireAsync(Test("SignInTests", "SignIn"), CancellationToken.None));

        exception.Message.ShouldStartWith(
            $"The BrowserStack session 'SignInTests.SignIn' did not start at wss://cdp.browserstack.com/playwright: the server does not run the Playwright version of this client, {AutomationSession.PlaywrightVersion} (both sides need the same major.minor);");
        exception.Message.ShouldContain("https://www.browserstack.com/docs/automate/playwright/browsers-and-os");
        exception.Message.ShouldContain("PlaywrightException: WebSocket error:");
    }

    [Fact]
    public async Task DisposeAsync_ClosesTheSessionsOfUnreleasedTests_Unmarked()
    {
        var provider = Provider();
        await provider.AcquireAsync(Test("SignInTests", "SignIn"), CancellationToken.None);
        var browser = connector.Connections.ShouldHaveSingleItem().Browser;

        await provider.DisposeAsync();

        browser.Closes.ShouldBe(1);
        browser.Evaluated.ShouldBeEmpty();
        connector.Disposed.ShouldBeTrue();
        await Should.ThrowAsync<ObjectDisposedException>(async () => await provider.AcquireAsync(Test("SignInTests", "SignIn"), CancellationToken.None));
    }

    [Fact]
    public async Task ReleaseAsync_IgnoresALeaseItDidNotHandOut()
    {
        await using var provider = Provider();
        var browser = new FakeRemoteBrowser();

        await provider.ReleaseAsync(new BrowserLease(browser.Object, "other", new Dictionary<string, string>()), null, CancellationToken.None);

        browser.Closes.ShouldBe(0);
    }

    private static TestIdentity Test(string className, string method)
    {
        return new TestIdentity($"42.Tests/Sample.Tests.{className}/{method}", method, $"Sample.Tests.{className}", method, "42.Tests", [], []);
    }

    private static TestResultReport Report(TestIdentity test, TestOutcome outcome, string? message = null)
    {
        return TestData.Report(test, outcome) with
        {
            Failure = message is null ? null : new TestFailure(message, "PlaywrightException", null, null, null),
        };
    }

    private static Dictionary<string, string> Caps(string endpoint)
    {
        var caps = endpoint[(endpoint.IndexOf("caps=", StringComparison.Ordinal) + 5)..];
        return JsonSerializer.Deserialize<Dictionary<string, string>>(Uri.UnescapeDataString(caps)).ShouldNotBeNull();
    }

    private static JsonElement Status(object? argument)
    {
        var payload = argument.ShouldBeOfType<string>();
        payload.ShouldStartWith(BrowserStackSessionStatus.ExecutorPrefix);
        using var document = JsonDocument.Parse(payload[BrowserStackSessionStatus.ExecutorPrefix.Length..]);
        document.RootElement.GetProperty("action").GetString().ShouldBe("setSessionStatus");
        return document.RootElement.GetProperty("arguments").Clone();
    }

    private static void ShouldHideTheAccessKey(string text)
    {
        text.ShouldNotContain(AccessKey);
        text.ShouldNotContain(Uri.EscapeDataString(AccessKey));
        text.ShouldNotContain(System.Net.WebUtility.UrlEncode(AccessKey));
        text.ShouldNotContain(JsonEncodedText.Encode(AccessKey).ToString());
        text.ShouldNotContain(Uri.EscapeDataString(JsonEncodedText.Encode(AccessKey).ToString()));
        text.ShouldNotContain("bs+Key");
        text.ShouldNotContain("bs%2BKey");
    }

    private BrowserStackBrowserProvider Provider(Action<BrowserStackSettings>? configure = null, IReadOnlyList<TestIdentity>? planned = null, int slowMoMs = 0)
    {
        var settings = new UiAutomationSettings
        {
            Target = new TargetSettings { BaseUrl = "https://admin.example.test" },
            Browser = new BrowserSettings { Provider = BrowserProviderKind.BrowserStack, SlowMoMs = slowMoMs },
            BrowserStack = new BrowserStackSettings { Username = "ada", AccessKey = AccessKey, Project = "2S Platform - admin UI" },
        };
        configure?.Invoke(settings.BrowserStack);

        var run = new RunNameValues { RunId = "20261031-142500-abcd", StartedAt = TestData.Start, Provider = "BrowserStack", Engine = "chromium", Machine = "BUILDBOX" };
        return new BrowserStackBrowserProvider(new BrowserProviderContext(settings, run) { PlannedTests = planned ?? [], Logger = logger, TimeProvider = time }, connector);
    }
}
