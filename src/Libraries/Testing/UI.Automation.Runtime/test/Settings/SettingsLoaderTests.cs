using System.IO.Abstractions.TestingHelpers;
using _42.Testing.UI.Automation.Runtime.Settings;
using _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;
using Microsoft.Extensions.Configuration.UserSecrets;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Settings;

public sealed class SettingsLoaderTests
{
    private const string SecretsId = "42-ui-automation-runtime-unit-tests";

    private const string Configured = """
        {
          "Target": { "BaseUrl": "https://admin.example.test" },
          "Users": { "Pool": [ { "Email": "ui-01@example.test", "Roles": [ "Owner" ] }, { "Email": "ui-02@example.test" } ] }
        }
        """;

    private static readonly string BaseDirectory = MockUnixSupport.Path(@"C:\suite\bin");

    [Fact]
    public void Load_AppliesTheLayersInOrder()
    {
        var files = Files(
            ("testsettings.json", """{ "Target": { "BaseUrl": "https://admin.example.test" }, "Execution": { "Retries": 1 }, "Browser": { "SlowMoMs": 10 } }"""),
            ("testsettings.mobile.json", """{ "Execution": { "Retries": 2 }, "Browser": { "Device": "Pixel 7" } }"""),
            ("testsettings.local.json", """{ "Execution": { "Retries": 3 }, "Browser": { "Headless": false } }"""));
        files.AddFile(PathHelper.GetSecretsPathFromSecretsId(SecretsId), new MockFileData("""{ "Execution:Retries": "4", "Users:Pool:0:Email": "ui-01@example.test", "Users:Pool:0:Password": "pw-from-secrets" }"""));

        var settings = Load(
            files,
            environment: new Dictionary<string, string?>
            {
                ["UITESTS_PROFILE"] = "mobile",
                ["UITESTS_Execution__Retries"] = "5",
                ["UITESTS_Browser__Locale"] = "de-DE",
                ["OTHER_Browser__Locale"] = "fr-FR",
            },
            parameters: [new("Execution:Retries", "6"), new("Browser__TimeZone", "Europe/Prague")],
            secretsId: SecretsId);

        settings.Execution.Retries.ShouldBe(6);
        settings.Browser.SlowMoMs.ShouldBe(10);
        settings.Browser.Device.ShouldBe("Pixel 7");
        settings.Browser.Headless.ShouldBeFalse();
        settings.Users.Pool[0].Password.ShouldBe("pw-from-secrets");
        settings.Browser.Locale.ShouldBe("de-DE");
        settings.Browser.TimeZone.ShouldBe("Europe/Prague");
        settings.Profile.ShouldBe("mobile");
        settings.Sources.ShouldBe(["testsettings.json", "testsettings.mobile.json", "testsettings.local.json", "user secrets", "UITESTS_* variables", "parameters"]);
    }

    [Fact]
    public void Load_LetsTheEnvironmentWinOverTheFilesAndTheUserSecrets()
    {
        var files = Files(("testsettings.json", Configured), ("testsettings.local.json", """{ "Execution": { "Retries": 3 } }"""));
        files.AddFile(PathHelper.GetSecretsPathFromSecretsId(SecretsId), new MockFileData("""{ "Execution": { "Retries": 4 } }"""));

        var settings = Load(files, environment: new Dictionary<string, string?> { ["uitests_execution__retries"] = "5" }, secretsId: SecretsId);

        settings.Execution.Retries.ShouldBe(5);
    }

    [Fact]
    public void Load_TakesTheProfileParameterOverTheVariable_AndTheOptionOverBoth()
    {
        var files = Files(
            ("testsettings.json", Configured),
            ("testsettings.mobile.json", """{ "Browser": { "Device": "Pixel 7" } }"""),
            ("testsettings.firefox.json", """{ "Browser": { "Engine": "firefox" } }"""),
            ("testsettings.webkit.json", """{ "Browser": { "Engine": "webkit" } }"""));
        var environment = new Dictionary<string, string?> { ["UITESTS_PROFILE"] = "mobile" };

        var fromParameter = Load(files, environment: environment, parameters: [new("Profile", "firefox")]);
        var fromOption = Load(files, environment: environment, parameters: [new("Profile", "firefox")], profile: "webkit");

        fromParameter.Profile.ShouldBe("firefox");
        fromParameter.Browser.Engine.ShouldBe(BrowserEngine.Firefox);
        fromParameter.Browser.Device.ShouldBeNull();
        fromOption.Browser.Engine.ShouldBe(BrowserEngine.Webkit);
    }

    [Fact]
    public void Load_AppliesSeveralProfilesInOrder()
    {
        var files = Files(
            ("testsettings.json", Configured),
            ("testsettings.mobile.json", """{ "Browser": { "Device": "Pixel 7", "Engine": "firefox" } }"""),
            ("testsettings.reporting.json", """{ "Browser": { "Engine": "webkit" }, "Reporting": { "Enabled": true } }"""));

        var settings = Load(files, profile: "mobile, reporting");

        settings.Browser.Device.ShouldBe("Pixel 7");
        settings.Browser.Engine.ShouldBe(BrowserEngine.Webkit);
        settings.Reporting.Enabled.ShouldBeTrue();
        settings.Sources.ShouldContain("testsettings.reporting.json");
    }

    [Fact]
    public void Load_FailsForAMissingProfileFile_EvenWithoutATarget()
    {
        var files = Files(("testsettings.json", "{}"));

        var exception = Should.Throw<UiAutomationSettingsException>(() => Load(files, profile: "nope"));

        exception.Errors.ShouldHaveSingleItem().ShouldStartWith("The profile 'nope' has no testsettings.nope.json in ");
    }

    [Fact]
    public void Load_DoesNotPutTheProfileVariableIntoTheConfiguration()
    {
        var files = Files(("testsettings.json", Configured), ("testsettings.mobile.json", "{}"));

        var settings = Load(files, environment: new Dictionary<string, string?> { ["UITESTS_PROFILE"] = "mobile" });

        settings.Configuration["PROFILE"].ShouldBeNull();
    }

    [Fact]
    public void Load_WithoutBaseUrl_IsInSkipMode_AndValidatesNothing()
    {
        var files = Files(("testsettings.json", """
            {
              "Browser": { "Engine": "netscape", "SlowMoMs": -1 },
              "Execution": { "Retries": 99, "MaxParallelTests": 0 },
              "Users": { "Pool": [ { "Password": "x" } ] }
            }
            """));

        var settings = Load(files, setup: new FakeSetup { ValidationError = "never asked" });

        settings.IsConfigured.ShouldBeFalse();
        settings.Warnings.ShouldBeEmpty();
        UiAutomationSettings.NotConfiguredReason("src/Sample/README.md").ShouldBe("UITESTS_Target__BaseUrl is not configured; see src/Sample/README.md.");
    }

    [Fact]
    public void Load_WithoutFiles_IsInSkipMode()
    {
        var settings = Load(new MockFileSystem());

        settings.IsConfigured.ShouldBeFalse();
        settings.Sources.ShouldBeEmpty();
        settings.Execution.MaxParallelTests.ShouldBe(4);
    }

    [Fact]
    public void Load_WithoutBaseUrl_StillValidatesTheReportingOfAReplay()
    {
        var files = Files(("testsettings.json", """{ "Reporting": { "ReplayFile": "results.jsonl", "BatchSize": 0 } }"""));

        var exception = Should.Throw<UiAutomationSettingsException>(() => Load(files));

        exception.Errors.ShouldBe(["Reporting:ReplayFile needs Reporting:Enabled=true.", "Reporting:BatchSize must be at least 1."]);
    }

    [Fact]
    public void Load_CollectsEveryError()
    {
        var files = Files(("testsettings.json", """
            {
              "Target": { "BaseUrl": "ftp://files.example.test" },
              "Browser": { "Engine": "firefox", "Channel": "chrome" },
              "Execution": { "MaxParallelTests": 0, "Retries": 11 },
              "Users": { "Pool": [ { "Email": "ui-01@example.test" }, { "Email": "UI-01@example.test" }, { "Password": "TopSecret!" } ] },
              "Reporting": { "Enabled": true, "BatchSize": 0, "RunName": "Nightly {Nope}" }
            }
            """));

        var exception = Should.Throw<UiAutomationSettingsException>(() => Load(files));

        exception.Errors.ShouldContain("Target:BaseUrl must be an absolute http or https URL; it is 'ftp://files.example.test'.");
        exception.Errors.ShouldContain("Browser:Channel 'chrome' needs Browser:Engine chromium; it is firefox.");
        exception.Errors.ShouldContain("Execution:MaxParallelTests must be at least 1.");
        exception.Errors.ShouldContain("Execution:Retries must be between 0 and 10.");
        exception.Errors.ShouldContain("Users:Pool:1:Email repeats Users:Pool:0:Email.");
        exception.Errors.ShouldContain("Users:Pool:2:Email is required.");
        exception.Errors.ShouldContain("Reporting:BatchSize must be at least 1.");
        exception.Errors.ShouldContain(error => error.StartsWith("Reporting:RunName: the placeholder {Nope} is unknown", StringComparison.Ordinal));
        exception.Message.ShouldStartWith("The UI automation settings are not valid:");
        exception.Message.ShouldContain("Read from: testsettings.json.");
        exception.Message.ShouldNotContain("TopSecret!");
    }

    [Fact]
    public void Load_ReportsAValueThatDoesNotBind()
    {
        var files = Files(("testsettings.json", """{ "Target": { "BaseUrl": "https://admin.example.test" }, "Browser": { "Engine": "netscape" } }"""));

        var exception = Should.Throw<UiAutomationSettingsException>(() => Load(files));

        exception.Errors.ShouldHaveSingleItem().ShouldContain("Browser:Engine");
    }

    [Fact]
    public void Load_RequiresTheBrowserStackCredentials_ForTheBrowserStackProvider()
    {
        var files = Files(("testsettings.json", """{ "Target": { "BaseUrl": "https://admin.example.test" }, "Browser": { "Provider": "BrowserStack" }, "BrowserStack": { "Browser": "netscape" } }"""));

        var exception = Should.Throw<UiAutomationSettingsException>(() => Load(files));

        exception.Errors.ShouldBe(
        [
            "BrowserStack:Username is required when Browser:Provider is BrowserStack; set it in the user secrets or UITESTS_BrowserStack__Username.",
            "BrowserStack:AccessKey is required when Browser:Provider is BrowserStack; set it in the user secrets or UITESTS_BrowserStack__AccessKey.",
            "BrowserStack:Browser 'netscape' is not one of: chrome, edge, playwright-chromium, playwright-firefox, playwright-webkit.",
        ]);
    }

    [Fact]
    public void Load_ChecksTheBrowserStackSection_WithoutShowingACapsQuery()
    {
        var files = Files(("testsettings.json", """
            {
              "Target": { "BaseUrl": "https://admin.example.test" },
              "Browser": { "Provider": "BrowserStack" },
              "BrowserStack": {
                "Username": "ada", "AccessKey": "bs-key-456", "Endpoint": "https://cdp.browserstack.com/playwright?caps=bs-key-456",
                "Os": "", "Console": "loud", "Build": "{Nightly}", "ConnectTimeoutSeconds": 0,
                "AdditionalCapabilities": { "browserstack.accessKey": "bs-key-456", "browserstack.idleTimeout": "300" }
              }
            }
            """));

        var exception = Should.Throw<UiAutomationSettingsException>(() => Load(files));

        exception.Errors.ShouldBe(
        [
            "BrowserStack:Endpoint must be an absolute ws or wss URL; it is 'https://cdp.browserstack.com/playwright'.",
            "BrowserStack:Os and BrowserStack:OsVersion are required, e.g. Windows and 11, or OS X and Tahoe.",
            "BrowserStack:Console 'loud' is not one of: disable, errors, warnings, info, verbose.",
            "BrowserStack:Build: the placeholder {Nightly} is unknown; use one of {RunId}, {Date}, {Time}, {Profile}, {Provider}, {Engine}, {Device}, {Branch}, {Sha7}, {BuildNumber}, {Machine}.",
            "BrowserStack:AdditionalCapabilities must not set browserstack.accessKey; use BrowserStack:Username and BrowserStack:AccessKey.",
            "BrowserStack:ConnectTimeoutSeconds must be at least 1.",
        ]);
        exception.Message.ShouldNotContain("bs-key-456");
    }

    [Fact]
    public void Load_RefusesAnEndpointWithItsOwnCaps()
    {
        var files = Files(("testsettings.json", """
            {
              "Target": { "BaseUrl": "https://admin.example.test" },
              "Browser": { "Provider": "BrowserStack" },
              "BrowserStack": { "Username": "ada", "AccessKey": "bs-key-456", "Endpoint": "wss://cdp.browserstack.com/playwright?caps=%7B%7D" }
            }
            """));

        var exception = Should.Throw<UiAutomationSettingsException>(() => Load(files));

        exception.Errors.ShouldHaveSingleItem().ShouldBe("BrowserStack:Endpoint must not have a caps parameter; the provider adds it from the BrowserStack section.");
    }

    [Fact]
    public void Load_BindsTheBrowserStackSection()
    {
        var files = Files(("testsettings.json", """
            {
              "Target": { "BaseUrl": "https://admin.example.test" },
              "Users": { "Pool": [ { "Email": "ui-01@example.test" } ] },
              "Browser": { "Provider": "BrowserStack" },
              "BrowserStack": {
                "Username": "ada", "Browser": "playwright-webkit", "Os": "OS X", "OsVersion": "Tahoe", "Endpoint": "ws://localhost:4242/",
                "BuildTag": "nightly", "AdditionalCapabilities": { "browserstack.idleTimeout": "300" }
              }
            }
            """));

        var settings = Load(files, environment: new Dictionary<string, string?> { ["UITESTS_BrowserStack__AccessKey"] = "bs-key-456" });

        settings.BrowserStack.AccessKey.ShouldBe("bs-key-456");
        settings.BrowserStack.Endpoint.ShouldBe("ws://localhost:4242/");
        settings.BrowserStack.BuildTag.ShouldBe("nightly");
        settings.BrowserStack.AdditionalCapabilities.ShouldBe(new Dictionary<string, string> { ["browserstack.idleTimeout"] = "300" });
        settings.EffectiveEngine.ShouldBe(BrowserEngine.Webkit);
        settings.Describe().ShouldContain("BrowserStack:AccessKey = ***");
        settings.Describe().ShouldNotContain("bs-key-456");
    }

    [Fact]
    public void Load_RequiresAPassword_WhenTheSignInFlowNeedsOne()
    {
        var files = Files(("testsettings.json", Configured));

        var exception = Should.Throw<UiAutomationSettingsException>(() => Load(files, setup: new FakeSetup()));

        exception.Errors.ShouldBe(
        [
            "Users:Pool:0:Password is required by the sign-in flow AuthKit; set it in the user secrets or UITESTS_Users__Pool__0__Password.",
            "Users:Pool:1:Password is required by the sign-in flow AuthKit; set it in the user secrets or UITESTS_Users__Pool__1__Password.",
        ]);
    }

    [Fact]
    public void Load_NeedsNoPassword_ForSignInNone_OrAFlowWithoutPasswords()
    {
        var files = Files(("testsettings.json", Configured));

        var none = Load(files, parameters: [new("Target:SignIn", "None")], setup: new FakeSetup());
        var passwordless = Load(files, setup: new FakeSetup { SignInFlows = [new FakeSignInFlow("MagicLink", requiresPassword: false)] });

        none.Target.SignIn.ShouldBe("None");
        passwordless.IsConfigured.ShouldBeTrue();
    }

    [Fact]
    public void Load_RejectsASignInNoFlowHas()
    {
        var files = Files(("testsettings.json", Configured));

        var exception = Should.Throw<UiAutomationSettingsException>(() => Load(files, parameters: [new("Target:SignIn", "Okta")], setup: new FakeSetup()));

        exception.Errors.ShouldContain("Target:SignIn 'Okta' names no sign-in flow; use one of: None, AuthKit.");
    }

    [Fact]
    public void Load_RunsTheValidationOfTheSetup()
    {
        var files = Files(("testsettings.json", Configured));

        var exception = Should.Throw<UiAutomationSettingsException>(
            () => Load(files, parameters: [new("Target:SignIn", "None")], setup: new FakeSetup { ValidationError = "Target:Organization is required." }));

        exception.Errors.ShouldBe(["Target:Organization is required."]);
    }

    [Fact]
    public void Load_NamesAFileThatIsNotJson()
    {
        var files = Files(("testsettings.json", Configured), ("testsettings.local.json", """{ "Execution": { """));

        var exception = Should.Throw<UiAutomationSettingsException>(() => Load(files));

        exception.Errors.ShouldHaveSingleItem().ShouldStartWith("testsettings.local.json is not valid JSON:");
    }

    [Fact]
    public void Load_AcceptsCommentsAndTrailingCommas()
    {
        var files = Files(("testsettings.json", """
            {
              // the target
              "Target": { "BaseUrl": "https://admin.example.test", },
            }
            """));

        Load(files).IsConfigured.ShouldBeTrue();
    }

    [Fact]
    public void Load_LowersTheParallelismToThePool_WithWarnings()
    {
        var files = Files(("testsettings.json", Configured));

        var settings = Load(files);

        settings.Execution.MaxParallelTests.ShouldBe(2);
        settings.Warnings.ShouldBe(
        [
            "Execution:MaxParallelTests 4 is more than the 2 users of Users:Pool; lowered to 2.",
            "Users:Pool has 2 users for 2 parallel tests: a test that leases a second user may wait up to Users:LeaseTimeoutSeconds (120 s).",
        ]);
    }

    [Fact]
    public void Load_DoesNotWarn_ForAPoolTwiceTheParallelism()
    {
        var files = Files(("testsettings.json", """
            {
              "Target": { "BaseUrl": "https://admin.example.test" },
              "Execution": { "MaxParallelTests": 2 },
              "Users": { "Pool": [ { "Email": "a@example.test" }, { "Email": "b@example.test" }, { "Email": "c@example.test" }, { "Email": "d@example.test" } ] }
            }
            """));

        Load(files).Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Load_BindsListsWithoutDefaults()
    {
        var files = Files(("testsettings.json", Configured));
        var withSystems = Load(files, parameters: [new("Reporting:Systems:0", "Testiny")]);

        Load(files).Reporting.Systems.ShouldBeEmpty();
        withSystems.Reporting.Systems.ShouldBe(["Testiny"]);
        withSystems.Users.Pool[0].Roles.ShouldBe(["Owner"]);
    }

    [Fact]
    public void Bind_ReadsTheConsumersOwnKeys_OncePerTypeAndSection()
    {
        var files = Files(("testsettings.json", """{ "Target": { "BaseUrl": "https://admin.example.test", "Organization": "acme", "SignIn": "None" } }"""));
        var settings = Load(files);

        var target = settings.Bind<ProbeTarget>("Target");

        target.Organization.ShouldBe("acme");
        target.BaseUrl.ShouldBe("https://admin.example.test");
        target.SignIn.ShouldBe(TargetSettings.NoSignIn);
        settings.Bind<ProbeTarget>("Target").ShouldBeSameAs(target);
    }

    [Fact]
    public void ResolvePath_IsRelativeToTheBaseDirectory()
    {
        var settings = Load(Files(("testsettings.json", Configured)));

        settings.ResolvePath("TestResults/artifacts").ShouldBe(Path.GetFullPath("TestResults/artifacts", BaseDirectory));
    }

    [Fact]
    public void Load_TakesThePoolOfTheHighestLayerWithAnEmail_AndAppliesThePasswordsByIndex()
    {
        var files = Files(
            ("testsettings.json", """
                {
                  "Target": { "BaseUrl": "https://admin.example.test" },
                  "Users": { "Pool": [ { "Email": "a@example.test" }, { "Email": "b@example.test" }, { "Email": "c@example.test", "Roles": [ "Owner" ] } ] }
                }
                """),
            ("testsettings.local.json", """{ "Users": { "Pool": [ { "Email": "x@example.test" }, { "Email": "y@example.test", "Roles": [ "Reader" ] } ] } }"""));
        files.AddFile(PathHelper.GetSecretsPathFromSecretsId(SecretsId), new MockFileData("""{ "Users:Pool:0:Password": "pw-0", "Users:Pool:1:Password": "pw-1", "Users:Pool:2:Password": "pw-orphan" }"""));

        var settings = Load(files, secretsId: SecretsId);

        settings.Users.Pool.Select(user => user.Email).ShouldBe(["x@example.test", "y@example.test"]);
        settings.Users.Pool.Select(user => user.Password).ShouldBe(["pw-0", "pw-1"]);
        settings.Users.Pool[1].Roles.ShouldBe(["Reader"]);
    }

    [Fact]
    public void Load_KeepsTheLowerPool_WhenAHigherLayerSetsOnlyPasswords()
    {
        var files = Files(("testsettings.json", Configured));
        var settings = Load(files, environment: new Dictionary<string, string?> { ["UITESTS_Users__Pool__1__Password"] = "pw-1" });

        settings.Users.Pool.Count.ShouldBe(2);
        settings.Users.Pool[0].Roles.ShouldBe(["Owner"]);
        settings.Users.Pool[1].Password.ShouldBe("pw-1");
    }

    [Fact]
    public void Load_LetsAProfilePoolReplaceTheCommittedOne()
    {
        var files = Files(
            ("testsettings.json", Configured),
            ("testsettings.mock.json", """{ "Users": { "Pool": [ { "Email": "m1@example.test" }, { "Email": "m2@example.test" }, { "Email": "m3@example.test" }, { "Email": "m4@example.test" } ] } }"""),
            ("testsettings.big.json", """{ "Users": { "Pool": [ { "Email": "b1@example.test" }, { "Email": "b2@example.test" }, { "Email": "b3@example.test" }, { "Email": "b4@example.test" }, { "Email": "b5@example.test" } ] } }"""));

        var settings = Load(files, profile: "big,mock");

        settings.Users.Pool.Select(user => user.Email).ShouldBe(["m1@example.test", "m2@example.test", "m3@example.test", "m4@example.test"]);
    }

    private static MockFileSystem Files(params (string Name, string Json)[] files)
    {
        var fileSystem = new MockFileSystem();
        foreach (var (name, json) in files)
        {
            fileSystem.AddFile(fileSystem.Path.Combine(BaseDirectory, name), new MockFileData(json));
        }

        return fileSystem;
    }

    private static UiAutomationSettings Load(
        MockFileSystem files,
        IReadOnlyDictionary<string, string?>? environment = null,
        KeyValuePair<string, string?>[]? parameters = null,
        IUiAutomationSetup? setup = null,
        string? secretsId = null,
        string? profile = null)
    {
        return UiAutomationSettingsLoader.Load(new UiAutomationSettingsOptions
        {
            BaseDirectory = BaseDirectory,
            FileSystem = files,
            EnvironmentVariables = environment ?? new Dictionary<string, string?>(),
            Parameters = parameters,
            Setup = setup,
            UserSecretsId = secretsId,
            Profile = profile,
        });
    }

    private sealed class ProbeTarget : TargetSettings
    {
        public string? Organization { get; set; }
    }
}
