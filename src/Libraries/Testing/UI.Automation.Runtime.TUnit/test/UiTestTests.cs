using TUnit.Core;
using TUnit.Core.Exceptions;

namespace _42.Testing.UI.Automation.Runtime.TUnit.UnitTests;

public sealed class UiTestTests
{
    [Fact]
    public void SkipWhenAsked_SkipsWithTheReasonOfGetSkipReason()
    {
        var test = new SkippingTest("Desktop only: the viewport is 412 px wide.");

        var skip = Should.Throw<SkipTestException>(() => test.SkipWhenAsked(null!, null!));

        skip.Reason.ShouldBe("Desktop only: the viewport is 412 px wide.");
    }

    [Fact]
    public void SkipWhenAsked_RunsTheTest_WithoutAReason()
    {
        Should.NotThrow(() => new SkippingTest(null).SkipWhenAsked(null!, null!));
        Should.NotThrow(() => new PlainTest().SkipWhenAsked(null!, null!));
    }

    [Fact]
    public void ASkipFromTheHook_IsReportedAsSkipped_AndNeverRetried()
    {
        var skip = Should.Throw<SkipTestException>(() => new SkippingTest("Phone only.").SkipWhenAsked(null!, null!));
        var fromHook = new BeforeTestException("BeforeTest hook failed", skip);

        TestOutcomeMapper.Map(TestState.Failed, fromHook).ShouldBe(TestOutcome.Skipped);
        RetryPolicy.ShouldRetry(fromHook).ShouldBeFalse();
    }

    // The hook's arguments are not read by these tests' overrides, so they get null.
    private sealed class SkippingTest(string? reason) : UiTest
    {
        protected override string? GetSkipReason(global::TUnit.Core.TestContext context, AutomationSession session)
        {
            return reason;
        }
    }

    private sealed class PlainTest : UiTest;
}
