using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Commands.Account;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Services;
using _42.Platform.Cli.UnitTests.Authentication;
using _42.Platform.Storyteller.Sdk;
using Microsoft.Extensions.Options;
using Moq;
using Sharprompt;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

// The user sign-in of sform account: the device code prompt (text, and the JSON line of SFORM_DEVICE_CODE_OUTPUT), -b
// with SFORM_BROWSER, -o, several organizations without a terminal, and the exit codes of a failed sign-in.
public class AccountCommandSignInTests
{
    private static readonly string Home = MockUnixSupport.Path(@"C:\state\ada");

    private static readonly DeviceCodePrompt Prompt = new(
        "BCDF-GHJK",
        new Uri("https://auth.example.com/device"),
        new Uri("https://auth.example.com/device?user_code=BCDF-GHJK&x=1"),
        TimeSpan.FromSeconds(599));

    private static readonly SignedInUser Ada = new("user_01", "ada@example.com", "Ada Lovelace");

    private readonly RecordingConsole _console = new();
    private readonly Mock<IAccessApiClient> _accessApi = new(MockBehavior.Strict);
    private readonly Mock<IAuthenticationService> _authentication = new(MockBehavior.Strict);
    private readonly Mock<IBrowserLauncher> _browser = new(MockBehavior.Strict);
    private readonly Dictionary<string, string> _environment = new();

    public AccountCommandSignInTests()
    {
        _authentication
            .Setup(authentication => authentication.GetSignedInUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((SignedInUser?)null);
        _accessApi
            .Setup(api => api.GetAccountAsync())
            .ReturnsAsync(new _42.Platform.Storyteller.Sdk.Account
            {
                Id = "acc-7",
                Name = "Ada Lovelace",
                UserName = "ada@example.com",
                AccessMap = new Dictionary<string, AccountRole> { ["house.stored"] = AccountRole.Owner },
            });
    }

    [Fact]
    public async Task DeviceCode_PrintsTheAddressTheCodeTheCompleteAddressAndTheExpiry()
    {
        SignInShowing();
        _console.UseInnerConsole(redirectedInput: true);

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldContain("To sign in, open https://auth.example.com/device and enter the code BCDF-GHJK.");
        _console.Lines.ShouldContain("Or open https://auth.example.com/device?user_code=BCDF-GHJK&x=1 with the code filled in.");
        _console.Lines.ShouldContain("The code expires in 10 minute(s).");
        _console.Lines.ShouldContain("! You have been logged in as ada@example.com");

        // No JSON line without SFORM_DEVICE_CODE_OUTPUT.
        _console.ErrorText.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("json")]
    [InlineData("JSON")]
    public async Task DeviceCode_WithJsonOutput_WritesOneJsonLineOnStandardError(string value)
    {
        _environment[SformVariables.DEVICE_CODE_OUTPUT] = value;
        SignInShowing();
        _console.UseInnerConsole(redirectedInput: true);

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _console.ErrorText.ShouldBe(
            """{"userCode":"BCDF-GHJK","verificationUri":"https://auth.example.com/device","verificationUriComplete":"https://auth.example.com/device?user_code=BCDF-GHJK&x=1","expiresIn":599}""" + "\n");
        _console.Lines.ShouldContain("The code expires in 10 minute(s).");
    }

    // A program reads the contract before any themed output (which, without a console, used to be where sform crashed).
    [Fact]
    public async Task DeviceCode_WithJsonOutput_WritesTheJsonLineBeforeTheHeader()
    {
        _environment[SformVariables.DEVICE_CODE_OUTPUT] = "json";
        SignInShowing();
        _console.UseInnerConsole(redirectedInput: true);
        string? errorAtTheFirstDocument = null;
        _console.Mock
            .Setup(console => console.WriteExactDocument(It.IsAny<Alba.CsConsoleFormat.Document>()))
            .Callback(() => errorAtTheFirstDocument ??= _console.ErrorText);

        await Execute();

        errorAtTheFirstDocument.ShouldNotBeNull().ShouldStartWith("""{"userCode":"BCDF-GHJK",""");
    }

    [Fact]
    public async Task DeviceCode_WithAnotherOutputValue_WritesNoJson()
    {
        _environment[SformVariables.DEVICE_CODE_OUTPUT] = "text";
        SignInShowing();
        _console.UseInnerConsole(redirectedInput: true);

        await Execute();

        _console.ErrorText.ShouldBeEmpty();
    }

    [Fact]
    public async Task Browser_OpensTheCompleteAddress()
    {
        SignInShowing();
        _browser.Setup(browser => browser.TryOpen(Prompt.VerificationUriComplete!)).Returns(true);

        var exitCode = await Execute(openBrowser: true);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _browser.Verify(browser => browser.TryOpen(Prompt.VerificationUriComplete!), Times.Once);
        _console.Lines.ShouldNotContain("The browser could not be opened, please open the address manually.");
    }

    [Fact]
    public async Task Browser_ThatCannotBeStarted_SaysToOpenTheAddressAndTheSignInGoesOn()
    {
        SignInShowing();
        _browser.Setup(browser => browser.TryOpen(It.IsAny<Uri>())).Returns(false);

        var exitCode = await Execute(openBrowser: true);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldContain("The browser could not be opened, please open the address manually.");
        _console.Lines.ShouldContain("! You have been logged in as ada@example.com");
    }

    [Fact]
    public async Task Browser_WithoutTheCompleteAddress_OpensTheVerificationAddress()
    {
        var prompt = Prompt with { VerificationUriComplete = null };
        SignInShowing(prompt);
        _browser.Setup(browser => browser.TryOpen(prompt.VerificationUri)).Returns(true);

        await Execute(openBrowser: true);

        _browser.Verify(browser => browser.TryOpen(prompt.VerificationUri), Times.Once);
    }

    [Fact]
    public async Task Organization_IsPassedToTheSignIn()
    {
        _authentication
            .Setup(authentication => authentication.LoginWithDeviceCodeAsync(
                It.IsAny<Func<DeviceCodePrompt, Task>>(),
                It.IsAny<Func<IReadOnlyList<OrganizationChoice>, Task<string>>?>(),
                "org_02",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ada);

        var exitCode = await Execute(organization: " org_02 ");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
    }

    // -o is never ignored: a stored session of the organization asked for goes on, any other stops before any call.
    [Fact]
    public async Task Organization_OfTheStoredSession_GoesOn()
    {
        StoredSession(Ada with { OrganizationId = "org_02" });

        var exitCode = await Execute(organization: "ORG_02");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldContain("! You are already logged in as ada@example.com");
    }

    [Theory]
    [InlineData("org_01", "! You are signed in as ada@example.com to the organization 'org_01', and -o|--organization asks for 'org_02'.")]
    [InlineData(null, "! You are signed in as ada@example.com without an organization, and -o|--organization asks for 'org_02'.")]
    public async Task Organization_OtherThanTheStoredSession_IsAWrongInput(string? stored, string line)
    {
        StoredSession(Ada with { OrganizationId = stored });

        var exitCode = await Execute(organization: "org_02");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldContain(line);
        _console.Lines.ShouldContain("Sign out with sform account logout, then sign in with -o.");
        _accessApi.Verify(api => api.GetAccountAsync(), Times.Never);
    }

    [Fact]
    public async Task SeveralOrganizations_OnRedirectedInput_AreListedWithTheOption_AndNothingIsAsked()
    {
        _console.UseInnerConsole(redirectedInput: true);
        _authentication
            .Setup(authentication => authentication.LoginWithDeviceCodeAsync(
                It.IsAny<Func<DeviceCodePrompt, Task>>(),
                It.IsAny<Func<IReadOnlyList<OrganizationChoice>, Task<string>>?>(),
                null,
                It.IsAny<CancellationToken>()))
            .Returns<Func<DeviceCodePrompt, Task>, Func<IReadOnlyList<OrganizationChoice>, Task<string>>?, string?, CancellationToken>(
                async (onPrompt, select, _, _) =>
                {
                    await onPrompt(Prompt);
                    await select!([new OrganizationChoice("org_01", "Acme"), new OrganizationChoice("org_02", "Contoso")]);
                    return Ada;
                });

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.ErrorLines.ShouldBe(
        [
            "! sform cannot ask for the organization to sign in to: the input is redirected.",
            "  You belong to several organizations; pass one with -o|--organization <id>:",
            "    org_01  Acme",
            "    org_02  Contoso",
        ]);
        _console.Mock.Verify(console => console.Select(It.IsAny<SelectOptions<OrganizationChoice>>()), Times.Never);
        _accessApi.Verify(api => api.GetAccountAsync(), Times.Never);
    }

    [Fact]
    public async Task SeveralOrganizations_InATerminal_AreOffered()
    {
        _console.UseInnerConsole(redirectedInput: false);
        _console.Mock.Setup(console => console.Select(It.IsAny<SelectOptions<OrganizationChoice>>())).Returns(new OrganizationChoice("org_02", "Contoso"));
        string? chosen = null;
        _authentication
            .Setup(authentication => authentication.LoginWithDeviceCodeAsync(
                It.IsAny<Func<DeviceCodePrompt, Task>>(),
                It.IsAny<Func<IReadOnlyList<OrganizationChoice>, Task<string>>?>(),
                null,
                It.IsAny<CancellationToken>()))
            .Returns<Func<DeviceCodePrompt, Task>, Func<IReadOnlyList<OrganizationChoice>, Task<string>>?, string?, CancellationToken>(
                async (_, select, _, _) =>
                {
                    chosen = await select!([new OrganizationChoice("org_01", "Acme"), new OrganizationChoice("org_02", "Contoso")]);
                    return Ada;
                });

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        chosen.ShouldBe("org_02");
    }

    [Theory]
    [InlineData(AuthenticationFailureReason.Denied, ExitCodes.WARNING_UNAUTHORIZED_ACCESS, "! The sign-in was declined.")]
    [InlineData(AuthenticationFailureReason.Configuration, ExitCodes.ERROR_WRONG_INPUT, "! You are not a member of the organization 'org_99'.")]
    [InlineData(AuthenticationFailureReason.ServiceError, ExitCodes.ERROR_SERVICE_FAILURE, "! The log in operation failed, please try it again later.")]
    [InlineData(AuthenticationFailureReason.Expired, ExitCodes.ERROR_WRONG_INPUT, "! The sign-in code expired before the sign-in was completed, please try it again.")]
    [InlineData(AuthenticationFailureReason.Cancelled, ExitCodes.ERROR_WRONG_INPUT, "! The log in operation has been cancelled, please try it again later.")]
    public async Task FailedSignIn_EndsWithTheCodeOfItsReason(AuthenticationFailureReason reason, int expectedExitCode, string line)
    {
        _authentication
            .Setup(authentication => authentication.LoginWithDeviceCodeAsync(
                It.IsAny<Func<DeviceCodePrompt, Task>>(),
                It.IsAny<Func<IReadOnlyList<OrganizationChoice>, Task<string>>?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AuthenticationException(reason, "You are not a member of the organization 'org_99'."));

        var exitCode = await Execute(organization: "org_99");

        exitCode.ShouldBe(expectedExitCode);
        _console.Lines.ShouldContain(line);
        _accessApi.Verify(api => api.GetAccountAsync(), Times.Never);
    }

    [Fact]
    public async Task FileTokenStore_WithHome_IsAnnounced()
    {
        _environment[SformVariables.TOKEN_STORE] = "file";
        SignInShowing();

        await Execute(home: Home);

        _console.Lines.ShouldContain($"! The session is kept unencrypted in {Home} (SFORM_TOKEN_STORE=file); keep the folder private.");
    }

    [Fact]
    public async Task FileTokenStore_WithoutHome_IsIgnoredWithAWarning()
    {
        _environment[SformVariables.TOKEN_STORE] = "file";
        SignInShowing();

        await Execute();

        _console.Lines.ShouldContain("! SFORM_TOKEN_STORE=file is ignored without SFORM_HOME; the session is kept in the protected store.");
    }

    [Fact]
    public async Task NoTokenStoreVariable_NoNotice()
    {
        SignInShowing();

        await Execute(home: Home);

        _console.Lines.ShouldNotContain(line => line.Contains("SFORM_TOKEN_STORE"));
    }

    private void StoredSession(SignedInUser user)
    {
        _authentication
            .Setup(authentication => authentication.GetSignedInUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
    }

    private void SignInShowing(DeviceCodePrompt? prompt = null)
    {
        _authentication
            .Setup(authentication => authentication.LoginWithDeviceCodeAsync(
                It.IsAny<Func<DeviceCodePrompt, Task>>(),
                It.IsAny<Func<IReadOnlyList<OrganizationChoice>, Task<string>>?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<DeviceCodePrompt, Task>, Func<IReadOnlyList<OrganizationChoice>, Task<string>>?, string?, CancellationToken>(
                async (onPrompt, _, _, _) =>
                {
                    await onPrompt(prompt ?? Prompt);
                    return Ada;
                });
    }

    private Task<int> Execute(bool openBrowser = false, string? organization = null, string? home = null)
    {
        var fileSystem = new MockFileSystem();
        var paths = CliPaths.Create(fileSystem, home, MockUnixSupport.Path(@"C:\tools\sform"), () => MockUnixSupport.Path(@"C:\Users\ada"));
        var machine = TestMachines.Create(new Dictionary<string, string>(), fileSystem, new ScriptedHttpHandler());
        var stored = Options.Create(new AccessDefaultOptions { OrganizationName = "house", ProjectName = "stored" });
        var command = new AccountCommand(
            _console.Object,
            _accessApi.Object,
            _authentication.Object,
            stored,
            machine,
            new CommandContext(stored, machine),
            SformEnvironment.FromEnvironment(name => _environment.GetValueOrDefault(name), paths),
            _browser.Object)
        {
            OpenBrowser = openBrowser,
            Organization = organization,
        };

        return command.OnExecuteAsync();
    }
}
