using System.Text.Json;
using _42.Testing.UI.Automation.Runtime.Browsers;
using _42.Testing.UI.Automation.Runtime.Reporting;
using _42.Testing.UI.Automation.Runtime.Settings;
using _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Browsers;

public sealed class BrowserStackCapabilitiesTests
{
    [Fact]
    public void Create_HasEveryCapability_InOrder_AsStrings()
    {
        var settings = new BrowserStackSettings
        {
            Username = "ada",
            AccessKey = "key-123",
            Project = "2S Platform - admin UI",
            BuildTag = "nightly",
            Local = true,
            LocalIdentifier = "ci-42",
            PlaywrightVersion = "1.latest",
            NetworkLogs = true,
            Debug = true,
        };

        var capabilities = BrowserStackCapabilities.Create(settings, "SignInTests.SignIn_ShowsOrganizations", "local BUILDBOX 2026-10-31", "1.63.0");

        capabilities.ShouldBe(
        [
            new("browser", "chrome"),
            new("browser_version", "latest"),
            new("os", "Windows"),
            new("os_version", "11"),
            new("resolution", "1920x1080"),
            new("project", "2S Platform - admin UI"),
            new("build", "local BUILDBOX 2026-10-31"),
            new("buildTag", "nightly"),
            new("name", "SignInTests.SignIn_ShowsOrganizations"),
            new("browserstack.username", "ada"),
            new("browserstack.accessKey", "key-123"),
            new("browserstack.local", "true"),
            new("browserstack.localIdentifier", "ci-42"),
            new("browserstack.playwrightVersion", "1.latest"),
            new("client.playwrightVersion", "1.63.0"),
            new("browserstack.console", "errors"),
            new("browserstack.networkLogs", "true"),
            new("browserstack.debug", "true"),
            new("browserstack.video", "true"),
        ]);
    }

    [Fact]
    public void Create_LeavesOutTheBrowserVersionOfAPlaywrightBuild_AndWhatIsNotSet()
    {
        var settings = new BrowserStackSettings
        {
            Username = "ada",
            AccessKey = "key-123",
            Browser = "Playwright-WebKit",
            Os = "OS X",
            OsVersion = "Tahoe",
            Resolution = null,
            Console = "Info",
            Video = false,
        };

        var capabilities = BrowserStackCapabilities.Create(settings, "SignInTests", "build", "1.63.0").ToDictionary();

        capabilities["browser"].ShouldBe("playwright-webkit");
        capabilities["os"].ShouldBe("OS X");
        capabilities["os_version"].ShouldBe("Tahoe");
        capabilities["browserstack.local"].ShouldBe("false");
        capabilities["browserstack.console"].ShouldBe("info");
        capabilities["browserstack.video"].ShouldBe("false");
        capabilities.Keys.ShouldNotContain("browser_version");
        capabilities.Keys.ShouldNotContain("resolution");
        capabilities.Keys.ShouldNotContain("project");
        capabilities.Keys.ShouldNotContain("buildTag");
        capabilities.Keys.ShouldNotContain("browserstack.localIdentifier");
        capabilities.Keys.ShouldNotContain("browserstack.playwrightVersion");
        capabilities["client.playwrightVersion"].ShouldBe("1.63.0");
    }

    [Fact]
    public void Create_AppliesTheAdditionalCapabilities_OverTheOthers_ButNeverTheCredentials()
    {
        var settings = new BrowserStackSettings
        {
            Username = "ada",
            AccessKey = "key-123",
            AdditionalCapabilities = new Dictionary<string, string>
            {
                ["browserstack.idleTimeout"] = "300",
                ["resolution"] = "2560x1440",
                ["browserstack.accessKey"] = "other",
                ["BrowserStack.Username"] = "other",
            },
        };

        var capabilities = BrowserStackCapabilities.Create(settings, "name", "build", "1.63.0");

        capabilities.Single(capability => capability.Key == "resolution").Value.ShouldBe("2560x1440");
        capabilities[^1].ShouldBe(new("browserstack.idleTimeout", "300"));
        capabilities.Single(capability => capability.Key == BrowserStackCapabilities.AccessKeyKey).Value.ShouldBe("key-123");
        capabilities.Single(capability => capability.Key == BrowserStackCapabilities.UsernameKey).Value.ShouldBe("ada");
        capabilities.Count(capability => capability.Key.Contains("username", StringComparison.OrdinalIgnoreCase)).ShouldBe(1);
    }

    [Fact]
    public void CreateEndpoint_UrlEncodesTheJson_SoItReadsBackUnchanged()
    {
        var settings = new BrowserStackSettings
        {
            Username = "ada",
            AccessKey = "a+b/c=d e&f\"g",
            Project = "Ünïcode – \"quoted\" & <tagged>",
        };
        var capabilities = BrowserStackCapabilities.Create(settings, "Tests.Data(1, \"one\")", "feat/424-133 #57", "1.63.0");

        var endpoint = BrowserStackCapabilities.CreateEndpoint(BrowserStackSettings.DefaultEndpoint, BrowserStackCapabilities.ToJson(capabilities));

        endpoint.ShouldStartWith("wss://cdp.browserstack.com/playwright?caps=");
        var caps = endpoint["wss://cdp.browserstack.com/playwright?caps=".Length..];
        caps.ShouldNotContain(" ");
        caps.ShouldNotContain("&");
        caps.ShouldNotContain("+");
        caps.ShouldNotContain("\"");
        using var document = JsonDocument.Parse(Uri.UnescapeDataString(caps));
        var root = document.RootElement;
        root.GetProperty("browserstack.accessKey").GetString().ShouldBe("a+b/c=d e&f\"g");
        root.GetProperty("project").GetString().ShouldBe("Ünïcode – \"quoted\" & <tagged>");
        root.GetProperty("name").GetString().ShouldBe("Tests.Data(1, \"one\")");
        root.GetProperty("build").GetString().ShouldBe("feat/424-133 #57");
        root.EnumerateObject().Select(property => property.Value.ValueKind).Distinct().ShouldBe([JsonValueKind.String]);
    }

    [Theory]
    [InlineData("ws://localhost:4242/", "ws://localhost:4242/?caps=%7B%7D")]
    [InlineData("wss://grid.example.test/playwright?region=eu", "wss://grid.example.test/playwright?region=eu&caps=%7B%7D")]
    [InlineData("wss://grid.example.test/playwright?", "wss://grid.example.test/playwright?caps=%7B%7D")]
    public void CreateEndpoint_AddsTheCapsParameter(string endpoint, string expected)
    {
        BrowserStackCapabilities.CreateEndpoint(endpoint, "{}").ShouldBe(expected);
    }

    [Fact]
    public void Describe_DropsTheQuery()
    {
        BrowserStackCapabilities.Describe("wss://cdp.browserstack.com/playwright?caps=%7B%22browserstack.accessKey%22%3A%22key%22%7D")
            .ShouldBe("wss://cdp.browserstack.com/playwright");
    }

    [Fact]
    public void ResolveBuild_IsLocalMachineAndDate_OutsideCi()
    {
        var run = Run(buildNumber: null);

        BrowserStackCapabilities.ResolveBuild(new BrowserStackSettings(), run).ShouldBe("local BUILDBOX 2026-10-31");
    }

    [Fact]
    public void ResolveBuild_IsBranchAndRunNumber_InAGitHubActionsJob()
    {
        var run = Run(buildNumber: "57");

        BrowserStackCapabilities.ResolveBuild(new BrowserStackSettings(), run).ShouldBe("feat/424-133 #57");
    }

    [Fact]
    public void ResolveBuild_FormatsTheConfiguredPattern()
    {
        var run = Run(buildNumber: "57");

        BrowserStackCapabilities.ResolveBuild(new BrowserStackSettings { Build = "Nightly {Date:yyyyMMdd} {Sha7} {Profile}" }, run).ShouldBe("Nightly 20261031 268a67a");
    }

    [Fact]
    public void SessionName_IsTheClassAndTheTest_OrTheClass()
    {
        var test = new TestIdentity("42.Tests/Sample.Tests.Outer+SignInTests/Data/Data(1, one)", "Data(1, one)", "Sample.Tests.Outer+SignInTests", "Data", "42.Tests", [], []);

        BrowserStackCapabilities.SessionName(test, perTest: true).ShouldBe("SignInTests.Data(1, one)");
        BrowserStackCapabilities.SessionName(test, perTest: false).ShouldBe("SignInTests");
        BrowserStackCapabilities.SessionName(TestData.Identity("SignIn_ShowsOrganizations"), perTest: true).ShouldBe("SampleTests.SignIn_ShowsOrganizations");
    }

    [Fact]
    public void SessionName_IsCut_At255Characters()
    {
        var test = TestData.Identity(new string('x', 300));

        BrowserStackCapabilities.SessionName(test, perTest: true).Length.ShouldBe(BrowserStackCapabilities.MaxNameLength);
    }

    private static RunNameValues Run(string? buildNumber)
    {
        return new RunNameValues
        {
            RunId = "20261031-142500-abcd",
            StartedAt = TestData.Start,
            Provider = "BrowserStack",
            Engine = "chromium",
            Branch = "feat/424-133",
            Sha = "268a67a2e4f1c0de",
            BuildNumber = buildNumber,
            Machine = "BUILDBOX",
        };
    }
}
