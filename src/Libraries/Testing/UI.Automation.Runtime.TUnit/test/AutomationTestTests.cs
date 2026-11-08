using _42.Testing.UI.Automation.Runtime.Browsers;
using _42.Testing.UI.Automation.Runtime.Users;
using TUnit.Core.Exceptions;

namespace _42.Testing.UI.Automation.Runtime.TUnit.UnitTests;

// An AutomationTest leases its user without a browser; a UiTest is one with its page; the session starts its browser
// provider up front only for a run with a UiTest.
public sealed class AutomationTestTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 8, 10, 15, 0, TimeSpan.Zero);

    [Fact]
    public void AnAutomationTest_LeasesItsUser_WithoutABrowser()
    {
        using var cancellation = new CancellationTokenSource();

        var options = new CommandTest().CreateScopeOptions(2, Start, cancellation.Token);

        options.Browser.ShouldBeFalse();
        options.SignedIn.ShouldBeFalse();
        options.LeaseUser.ShouldBeTrue();
        options.User.ShouldBeSameAs(CommandTest.Reader);
        options.Attempt.ShouldBe(2);
        options.StartedAt.ShouldBe(Start);
        options.TestCancellation.ShouldBe(cancellation.Token);
    }

    [Fact]
    public void AnAutomationTest_ThatUsesABrowser_StartsSignedIn_WhenItLeasesAUser()
    {
        new BrowserCommandTest(leasesUser: true).CreateScopeOptions(1, null, CancellationToken.None).ShouldSatisfyAllConditions(
            options => options.Browser.ShouldBeTrue(),
            options => options.SignedIn.ShouldBeTrue());
        new BrowserCommandTest(leasesUser: false).CreateScopeOptions(1, null, CancellationToken.None).ShouldSatisfyAllConditions(
            options => options.Browser.ShouldBeTrue(),
            options => options.SignedIn.ShouldBeFalse(),
            options => options.LeaseUser.ShouldBeFalse());
    }

    [Fact]
    public void AUiTest_OpensItsPage_SignedInUnlessItSignsInItself()
    {
        var signedIn = new PageTest(signedIn: true, leasesUser: true).CreateScopeOptions(1, null, CancellationToken.None);
        var signsInItself = new PageTest(signedIn: false, leasesUser: true).CreateScopeOptions(1, null, CancellationToken.None);
        var publicPage = new PageTest(signedIn: true, leasesUser: false).CreateScopeOptions(1, null, CancellationToken.None);

        signedIn.Browser.ShouldBeTrue();
        signedIn.SignedIn.ShouldBeTrue();
        signsInItself.Browser.ShouldBeTrue();
        signsInItself.SignedIn.ShouldBeFalse();
        publicPage.SignedIn.ShouldBeFalse();
        publicPage.LeaseUser.ShouldBeFalse();
    }

    [Fact]
    public void TheScope_IsThereOnlyOnceTheTestBegan()
    {
        var exception = Should.Throw<InvalidOperationException>(() => new CommandTest().ScopeBeforeBegin());

        exception.Message.ShouldBe("The test has not begun; use its scope from the test body or a Before(Test) hook of the derived class.");
    }

    [Fact]
    public void SkipWhenAsked_SkipsAnAutomationTest_WithTheReasonOfGetSkipReason()
    {
        var skip = Should.Throw<SkipTestException>(() => new CommandTest("Requires Target:Features byok.").SkipWhenAsked(null!, null!));

        skip.Reason.ShouldBe("Requires Target:Features byok.");
        Should.NotThrow(() => new CommandTest().SkipWhenAsked(null!, null!));
    }

    [Fact]
    public void TheSession_StartsTheBrowserProvider_WithTheSession_OnlyForARunWithAUiTest()
    {
        UiAutomationSession.ChooseBrowserProviderStart([typeof(CommandTest), typeof(PageTest)]).ShouldBe(BrowserProviderStart.WithSession);
        UiAutomationSession.ChooseBrowserProviderStart([typeof(CommandTest), typeof(BrowserCommandTest), typeof(PlainTest)]).ShouldBe(BrowserProviderStart.OnFirstLease);
        UiAutomationSession.ChooseBrowserProviderStart([]).ShouldBe(BrowserProviderStart.OnFirstLease);
    }

    [Fact]
    public void TheEndReceiversReport_NamesTheBrowser_OfAUiTest_NotOfAnAutomationTest_AndOfAPlainTestAsTheRun()
    {
        UiAutomationSession.ReportsBrowser(typeof(PageTest)).ShouldBe(true);
        UiAutomationSession.ReportsBrowser(typeof(CommandTest)).ShouldBe(false);
        UiAutomationSession.ReportsBrowser(typeof(BrowserCommandTest)).ShouldBe(false);
        UiAutomationSession.ReportsBrowser(typeof(PlainTest)).ShouldBeNull();
    }

    // A test of a command line: no browser, a Reader; GetSkipReason's arguments are not read, so the tests pass null.
    private sealed class CommandTest(string? skipReason = null) : AutomationTest
    {
        public static readonly UserRequirement Reader = UserRequirement.WithRole("Reader");

        protected override UserRequirement RequiredUser => Reader;

        public TestScope ScopeBeforeBegin()
        {
            return Scope;
        }

        protected override string? GetSkipReason(global::TUnit.Core.TestContext context, AutomationSession session)
        {
            return skipReason;
        }
    }

    private sealed class BrowserCommandTest(bool leasesUser) : AutomationTest
    {
        protected override bool UsesBrowser => true;

        protected override bool LeasesUser => leasesUser;
    }

    private sealed class PageTest(bool signedIn, bool leasesUser) : UiTest
    {
        protected override bool SignedIn => signedIn;

        protected override bool LeasesUser => leasesUser;
    }

    private sealed class PlainTest;
}
