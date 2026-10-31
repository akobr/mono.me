namespace _42.Testing.UI.Automation;

// The keys of TestRunDescriptor.Properties and TestResultReport.Properties the runtime fills; a key is left out when
// its value is unknown.
public static class TestRunProperties
{
    public const string RunId = "runId";
    public const string Profile = "profile";
    public const string Target = "target";
    public const string Provider = "provider";
    public const string Engine = "engine";
    public const string Channel = "channel";
    public const string BrowserVersion = "browserVersion";
    public const string Device = "device";
    public const string Headless = "headless";
    public const string Seed = "seed";
    public const string Branch = "branch";
    public const string Sha = "sha";
    public const string BuildNumber = "buildNumber";
    public const string BuildUrl = "buildUrl";
    public const string Machine = "machine";
    public const string TestEngine = "testEngine";
    public const string PlaywrightVersion = "playwrightVersion";

    // Per test only. User is a hash of the e-mail, never the address.
    public const string User = "user";
    public const string RemoteSession = "remoteSession";
    public const string Attempt = "attempt";
}
