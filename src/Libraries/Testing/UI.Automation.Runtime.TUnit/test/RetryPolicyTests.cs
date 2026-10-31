using TUnit.Core.Exceptions;

namespace _42.Testing.UI.Automation.Runtime.TUnit.UnitTests;

public sealed class RetryPolicyTests
{
    [Fact]
    public void ShouldRetry_ForAnOrdinaryFailure()
    {
        RetryPolicy.ShouldRetry(new InvalidOperationException("Locator expected to be visible")).ShouldBeTrue();
    }

    [Fact]
    public void ShouldRetry_NeverForASkip()
    {
        RetryPolicy.ShouldRetry(new SkipTestException("not configured")).ShouldBeFalse();
        RetryPolicy.ShouldRetry(new BeforeTestException("BeforeTest hook failed", new SkipTestException("not configured"))).ShouldBeFalse();
    }

    [Fact]
    public void ShouldRetry_NeverForADataConflict_EvenWrapped()
    {
        RetryPolicy.ShouldRetry(new DataConflictException("The key exists already.")).ShouldBeFalse();
        RetryPolicy.ShouldRetry(new TestExecutionException(new DataConflictException("The key exists already."), [], [])).ShouldBeFalse();
    }

    [Fact]
    public void ShouldRetry_NeverWhenTheSessionIsUnavailable_EvenWrapped()
    {
        var unavailable = new UiAutomationUnavailableException(
            "UI automation is not available: The target check failed: Target:Project 'nope' was not found.",
            new TargetCheckException("The target check failed: Target:Project 'nope' was not found."));

        RetryPolicy.ShouldRetry(unavailable).ShouldBeFalse();
        RetryPolicy.ShouldRetry(new BeforeTestException("BeforeTest hook failed", unavailable)).ShouldBeFalse();
    }
}
