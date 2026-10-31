using System.IO.Abstractions.TestingHelpers;
using _42.Testing.UI.Automation.Runtime.Settings;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Settings;

public sealed class SettingsRedactionTests
{
    [Theory]
    [InlineData("Users:Pool:0:Password", true)]
    [InlineData("BrowserStack:AccessKey", true)]
    [InlineData("Reporting:Testiny:ApiKey", true)]
    [InlineData("Custom:ClientSecret", true)]
    [InlineData("Custom:RefreshToken", true)]
    [InlineData("Secrets:Value", true)]
    [InlineData("users:pool:0:password", true)]
    [InlineData("Target:BaseUrl", false)]
    [InlineData("Users:Pool:0:Email", false)]
    [InlineData("BrowserStack:Username", false)]
    public void IsSecret_MatchesTheSecretWordsInAnySegment(string key, bool secret)
    {
        SettingsRedaction.IsSecret(key).ShouldBe(secret);
    }

    [Fact]
    public void Redact_MasksASecretWithAValue_AndKeepsAnEmptyOne()
    {
        SettingsRedaction.Redact("Users:Pool:0:Password", "pw-123").ShouldBe("***");
        SettingsRedaction.Redact("Users:Pool:0:Password", string.Empty).ShouldBe(string.Empty);
        SettingsRedaction.Redact("Target:BaseUrl", "https://admin.example.test").ShouldBe("https://admin.example.test");
    }

    [Fact]
    public void Describe_ShowsTheEffectiveSettings_WithoutSecrets()
    {
        var directory = MockUnixSupport.Path(@"C:\suite\bin");
        var files = new MockFileSystem();
        files.AddFile(files.Path.Combine(directory, "testsettings.json"), new MockFileData("""
            {
              "Target": { "BaseUrl": "https://admin.example.test", "Organization": "acme" },
              "Users": { "Pool": [ { "Email": "ui-01@example.test", "Password": "pw-123" }, { "Email": "ui-02@example.test", "Password": "" } ] },
              "BrowserStack": { "Username": "someone", "AccessKey": "bs-key-456" },
              "Reporting": { "Testiny": { "ApiKey": "testiny-789", "ProjectId": 7 } },
              "Custom": { "ClientSecret": "client-000" }
            }
            """));

        var settings = UiAutomationSettingsLoader.Load(new UiAutomationSettingsOptions
        {
            BaseDirectory = directory,
            FileSystem = files,
            EnvironmentVariables = new Dictionary<string, string?>(),
        });

        var description = settings.Describe();
        var lines = description.Split(Environment.NewLine);

        lines.ShouldContain("Users:Pool:0:Password = ***");
        lines.ShouldContain("Users:Pool:1:Password = ");
        description.ShouldContain("BrowserStack:AccessKey = ***");
        description.ShouldContain("Reporting:Testiny:ApiKey = ***");
        description.ShouldContain("Custom:ClientSecret = ***");
        description.ShouldNotContain("pw-123");
        description.ShouldNotContain("bs-key-456");
        description.ShouldNotContain("testiny-789");
        description.ShouldNotContain("client-000");
        description.ShouldContain("Target:BaseUrl = https://admin.example.test");
        description.ShouldContain("Target:Organization = acme");
        description.ShouldContain("Reporting:Testiny:ProjectId = 7");
        description.ShouldContain("Execution:ActionTimeoutMs = 10000");
        description.ShouldContain("Browser:ColorScheme = Light");
        description.ShouldContain("Browser:Headless = true");
    }

    [Fact]
    public void CreateRedactor_MasksEverySecretValue_AndThePoolEmailsOnlyWhenAsked()
    {
        var settings = new UiAutomationSettings
        {
            Users = new UsersSettings { Pool = [new PoolUserSettings { Email = "ui-01@example.test", Password = "pw-123\r\n" }] },
            BrowserStack = new BrowserStackSettings { AccessKey = "bs key/456" },
        };
        const string text = "fill(\"pw-123\") for ui-01@example.test, caps=bs%20key%2F456";

        SettingsRedaction.SecretValues(settings).ShouldBe(["pw-123\r\n", "bs key/456"]);
        SettingsRedaction.CreateRedactor(settings)(text).ShouldBe("fill(\"***\") for ui-01@example.test, caps=***");
        SettingsRedaction.CreateRedactor(settings, includeEmails: true)(text).ShouldBe("fill(\"***\") for ***, caps=***");
        SettingsRedaction.CreateRedactor(new UiAutomationSettings())(text).ShouldBe(text);
    }

    [Fact]
    public void StartBudget_CoversTheLease_ABrowserStackConnection_AndTheFirstSignIn()
    {
        var local = new UiAutomationSettings();
        var browserStack = new UiAutomationSettings { Browser = new BrowserSettings { Provider = BrowserProviderKind.BrowserStack } };
        var tuned = new UiAutomationSettings
        {
            Browser = new BrowserSettings { Provider = BrowserProviderKind.BrowserStack },
            BrowserStack = new BrowserStackSettings { ConnectTimeoutSeconds = 600 },
            Users = new UsersSettings { LeaseTimeoutSeconds = 30 },
            Execution = new ExecutionSettings { NavigationTimeoutMs = 60_000 },
        };

        local.StartBudget.ShouldBe(TimeSpan.FromSeconds(120 + 150 + 60));
        browserStack.StartBudget.ShouldBe(TimeSpan.FromSeconds(120 + 300 + 150 + 60));
        tuned.StartBudget.ShouldBe(TimeSpan.FromSeconds(30 + 600 + 300 + 60));
    }
}
