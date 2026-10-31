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
}
