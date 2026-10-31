using _42.Testing.UI.Automation.Reporting.Testiny.UnitTests.Fakes;

namespace _42.Testing.UI.Automation.Reporting.Testiny.UnitTests;

public sealed class TestinyResultCommentTests
{
    [Fact]
    public void Build_ShowsTheRemoteBrowser_AndItsSession()
    {
        var report = TestData.Result(
            TestData.Test("SignIn_ShowsOrganizations"),
            TestOutcome.Failed,
            failure: TestData.Failure(),
            properties: new Dictionary<string, string>
            {
                [TestRunProperties.Provider] = "BrowserStack",
                [TestRunProperties.Engine] = "chromium",
                [TestRunProperties.Channel] = "chrome",
                [TestRunProperties.BrowserVersion] = "142.0.7444.10",
                [TestRunProperties.Os] = "Windows 11",
                [TestRunProperties.RemoteSession] = "SampleTests.SignIn_ShowsOrganizations",
            });

        var text = TestinyResultComment.Build(report, TestData.Run(), TestinyResultStatus.Failed, [], TestinyOptions.DefaultMaxAttachmentBytes);

        text.ShouldContain("- **Browser:** chromium 142.0.7444.10, channel chrome, provider BrowserStack, os Windows 11\n");
        text.ShouldContain("- **Remote session:** SampleTests.SignIn\\_ShowsOrganizations\n");
    }
}
