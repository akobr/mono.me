using _42.Testing.UI.Automation.Runtime.Browsers;
using _42.Testing.UI.Automation.Runtime.Users;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace _42.Testing.UI.Automation.Runtime.TUnit.UnitTests;

public sealed class TestOutcomeMapperTests
{
    [Theory]
    [InlineData(TestState.Passed, TestOutcome.Passed)]
    [InlineData(TestState.Failed, TestOutcome.Failed)]
    [InlineData(TestState.Skipped, TestOutcome.Skipped)]
    [InlineData(TestState.Timeout, TestOutcome.Timeout)]
    [InlineData(TestState.Cancelled, TestOutcome.Cancelled)]
    [InlineData(TestState.Running, TestOutcome.Failed)]
    public void Map_TranslatesTheState(TestState state, TestOutcome expected)
    {
        TestOutcomeMapper.Map(state, null).ShouldBe(expected);
    }

    [Fact]
    public void Map_IsSkipped_ForASkipFromAHook()
    {
        var exception = new BeforeTestException("BeforeTest hook failed", new SkipTestException("not configured"));

        TestOutcomeMapper.Map(TestState.Failed, exception).ShouldBe(TestOutcome.Skipped);
    }

    [Fact]
    public void Map_IsBlocked_ForAMissingUserOrBrowser_EvenWrapped()
    {
        TestOutcomeMapper.Map(TestState.Failed, new BeforeTestException("BeforeTest hook failed", new UserLeaseException("No free user"))).ShouldBe(TestOutcome.Blocked);
        TestOutcomeMapper.Map(TestState.Failed, new TestExecutionException(new BrowserUnavailableException("gone"), [], [])).ShouldBe(TestOutcome.Blocked);
        TestOutcomeMapper.Map(TestState.Failed, new InvalidOperationException("assertion")).ShouldBe(TestOutcome.Failed);
    }

    [Fact]
    public void Unwrap_RemovesTheWrappersOfHookFailures()
    {
        var cause = new InvalidOperationException("cause");

        TestOutcomeMapper.Unwrap(new AfterTestException("AfterTest hook failed", cause)).ShouldBeSameAs(cause);
        TestOutcomeMapper.Unwrap(new TestExecutionException(null, [new BeforeTestException("hook", cause)], [])).ShouldBeSameAs(cause);
        TestOutcomeMapper.Unwrap(new AggregateException(cause)).ShouldBeSameAs(cause);
        TestOutcomeMapper.Unwrap(cause).ShouldBeSameAs(cause);
        TestOutcomeMapper.Unwrap(null).ShouldBeNull();
    }
}
