using System.Text.Json;
using _42.Testing.UI.Automation.Runtime.Browsers;
using _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Browsers;

public sealed class BrowserStackSessionStatusTests
{
    [Fact]
    public void Payload_IsTheExecutorCommand()
    {
        new BrowserStackSessionStatus(BrowserStackSessionStatus.Passed, null).Payload
            .ShouldBe("browserstack_executor: {\"action\":\"setSessionStatus\",\"arguments\":{\"status\":\"passed\"}}");
    }

    [Fact]
    public void Payload_EscapesTheReason()
    {
        var reason = "Expected \"Saved\" but was 'C:\\temp' </script> \u0001";

        var payload = new BrowserStackSessionStatus(BrowserStackSessionStatus.Failed, reason).Payload;

        payload.ShouldStartWith(BrowserStackSessionStatus.ExecutorPrefix);
        using var document = JsonDocument.Parse(payload[BrowserStackSessionStatus.ExecutorPrefix.Length..]);
        var arguments = document.RootElement.GetProperty("arguments");
        arguments.GetProperty("status").GetString().ShouldBe("failed");
        arguments.GetProperty("reason").GetString().ShouldBe(reason);
        payload.ShouldNotContain("</script>");
    }

    [Fact]
    public void ForTest_MarksAPassedTestPassed()
    {
        BrowserStackSessionStatus.ForTest(TestData.Report("Passes")).ShouldBe(new BrowserStackSessionStatus("passed", null));
    }

    [Fact]
    public void ForTest_MarksAFailedTestFailed_WithItsMessageOnOneLine()
    {
        var report = TestData.Report("Fails", TestOutcome.Failed) with
        {
            Failure = new TestFailure("Locator expected to be visible\r\nCall log:\n  - waiting for getByRole(Heading)", "PlaywrightException", null, null, null),
        };

        BrowserStackSessionStatus.ForTest(report)
            .ShouldBe(new BrowserStackSessionStatus("failed", "Locator expected to be visible Call log: - waiting for getByRole(Heading)"));
    }

    [Theory]
    [InlineData(TestOutcome.Timeout, "Timeout: Boom")]
    [InlineData(TestOutcome.Blocked, "Blocked: Boom")]
    [InlineData(TestOutcome.Cancelled, "Cancelled: Boom")]
    public void ForTest_MarksATestThatDidNotFinishFailed_WithItsOutcome(TestOutcome outcome, string reason)
    {
        var report = TestData.Report("Stops", outcome) with { Failure = new TestFailure("Boom", null, null, null, null) };

        BrowserStackSessionStatus.ForTest(report).ShouldBe(new BrowserStackSessionStatus("failed", reason));
    }

    [Fact]
    public void ForTest_NamesTheOutcome_WithoutAMessage()
    {
        BrowserStackSessionStatus.ForTest(TestData.Report("Stops", TestOutcome.Timeout)).ShouldBe(new BrowserStackSessionStatus("failed", "Timeout"));
    }

    [Fact]
    public void ForTest_LeavesASkippedTestOrNoResultUnmarked()
    {
        BrowserStackSessionStatus.ForTest(TestData.Report("Skips", TestOutcome.Skipped)).ShouldBeNull();
        BrowserStackSessionStatus.ForTest(null).ShouldBeNull();
    }

    [Fact]
    public void Cut_KeepsAtMost255Characters_WithAnEllipsis()
    {
        var cut = BrowserStackSessionStatus.Cut(new string('x', 300)).ShouldNotBeNull();

        cut.Length.ShouldBe(BrowserStackSessionStatus.MaxReasonLength);
        cut.ShouldEndWith("\u2026");
        BrowserStackSessionStatus.Cut(new string('x', 255)).ShouldBe(new string('x', 255));
        BrowserStackSessionStatus.Cut(" \n ").ShouldBeNull();
    }

    [Fact]
    public void ForTest_CutsALongMessage()
    {
        var report = TestData.Report("Fails", TestOutcome.Failed) with { Failure = new TestFailure(new string('m', 5000), null, null, null, null) };

        BrowserStackSessionStatus.ForTest(report).ShouldNotBeNull().Reason.ShouldNotBeNull().Length.ShouldBe(BrowserStackSessionStatus.MaxReasonLength);
    }

    [Fact]
    public void ForSession_IsFailed_WhenOneTestFailed_AndCountsThem()
    {
        var results = new[] { TestData.Report("First"), TestData.Report("Second", TestOutcome.Failed), TestData.Report("Third", TestOutcome.Skipped) };

        BrowserStackSessionStatus.ForSession(results).ShouldBe(new BrowserStackSessionStatus("failed", "1 of 3 test(s) failed; SampleTests.Second: Boom"));
    }

    [Fact]
    public void ForSession_IsPassed_WhenOneTestPassedAndNoneFailed_AndUnmarkedOtherwise()
    {
        BrowserStackSessionStatus.ForSession([TestData.Report("First"), TestData.Report("Second", TestOutcome.Skipped)]).ShouldBe(new BrowserStackSessionStatus("passed", null));
        BrowserStackSessionStatus.ForSession([TestData.Report("Second", TestOutcome.Skipped)]).ShouldBeNull();
        BrowserStackSessionStatus.ForSession([]).ShouldBeNull();
    }
}
