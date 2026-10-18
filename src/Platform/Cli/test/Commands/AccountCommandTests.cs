using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Commands.Account;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.UnitTests.Authentication;
using _42.Platform.Storyteller.Sdk;
using Microsoft.Extensions.Options;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

public class AccountCommandTests
{
    private const string ApiUrl = "https://storyteller.example.com/api";
    private const string TokenUrl = "https://auth.example.com/oauth2/token";

    private static readonly DateTimeOffset Start = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private readonly RecordingConsole _console = new();
    private readonly Mock<IAccessApiClient> _accessApi = new(MockBehavior.Strict);
    private readonly Mock<IAuthenticationService> _authentication = new(MockBehavior.Strict);
    private readonly ScriptedHttpHandler _http = new();
    private readonly Dictionary<string, string> _environment = new() { [StorytellerVariables.API_URL] = ApiUrl };

    [Fact]
    public async Task Machine_WithAnApiKey_SignsInWithoutAnyCall()
    {
        _environment[StorytellerVariables.API_KEY] = TestApiKeys.Create("acme", "billing", "3f2c");

        var exitCode = await Execute(openBrowser: true);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldBe(
        [
            "# Machine sign-in",
            "Signed in as machine 3f2c with an API key (STORYTELLER_API_KEY).",
            "Project acme.billing with default view.",
            $"API {ApiUrl}",
            "The API checks the credentials on the first call.",
        ]);
        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Machine_WithClientCredentials_GetsATokenAndShowsItsExpiry()
    {
        UseClientCredentials();
        _environment[StorytellerVariables.PROJECT] = "acme.billing";
        _environment[StorytellerVariables.VIEW] = "preview";
        _http.Respond(HttpStatusCode.OK, """{"access_token":"cc-1","expires_in":3600}""");

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldContain("Signed in as machine client_01 with client credentials (STORYTELLER_CLIENT_ID, STORYTELLER_CLIENT_SECRET, STORYTELLER_TOKEN_URL).");
        _console.Lines.ShouldContain("Project acme.billing with preview view.");
        _console.Lines.ShouldContain("Token valid until 2026-10-17T13:00:00.0000000+00:00.");
        _http.Requests.ShouldHaveSingleItem().Uri.ShouldBe(new Uri(TokenUrl));
    }

    [Fact]
    public async Task Machine_WithATokenFile_IsAnUnnamedMachine()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(MockUnixSupport.Path(@"C:\ci\token"), new MockFileData("opaque-token"));
        _environment[StorytellerVariables.TOKEN_FILE] = MockUnixSupport.Path(@"C:\ci\token");
        _environment[StorytellerVariables.PROJECT] = "acme.billing";

        var exitCode = await Execute(fileSystem: fileSystem);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldContain("Signed in as a machine with a token file (STORYTELLER_TOKEN_FILE).");
        _console.Lines.ShouldContain("The API checks the credentials on the first call.");
    }

    [Fact]
    public async Task Machine_Federated_WithAFile_ExchangesAndShowsTheMachineAndTheProject()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(MockUnixSupport.Path(@"C:\ci\platform-token"), new MockFileData("platform-token"));
        _environment[StorytellerVariables.CLIENT_ID] = "6d0f2c4e-0000-4000-8000-000000000001";
        _environment[StorytellerVariables.SUBJECT_TOKEN_FILE] = MockUnixSupport.Path(@"C:\ci\platform-token");
        _environment[StorytellerVariables.PROJECT] = "acme.billing";
        _http.Respond(HttpStatusCode.OK, """{"access_token":"st-1","token_type":"Bearer","expires_in":900}""");

        var exitCode = await Execute(fileSystem: fileSystem);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldBe(
        [
            "# Machine sign-in",
            "Signed in as machine 6d0f2c4e-0000-4000-8000-000000000001 with workload identity federation (STORYTELLER_CLIENT_ID, STORYTELLER_SUBJECT_TOKEN_FILE).",
            "Project acme.billing with default view.",
            $"API {ApiUrl}",
            "Token valid until 2026-10-17T12:15:00.0000000+00:00.",
        ]);
        _http.Requests.ShouldHaveSingleItem().Uri.ShouldBe(new Uri($"{ApiUrl}/v1/acme/billing/access/token"));
    }

    [Fact]
    public async Task Machine_WithAnExpiredTokenFile_IsRejected()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(MockUnixSupport.Path(@"C:\ci\token"), new MockFileData(TestTokens.AccessToken(Start.AddHours(-1))));
        _environment[StorytellerVariables.TOKEN_FILE] = MockUnixSupport.Path(@"C:\ci\token");
        _environment[StorytellerVariables.PROJECT] = "acme.billing";

        var exitCode = await Execute(fileSystem: fileSystem);

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        _console.Lines.ShouldBe(["! The token in STORYTELLER_TOKEN_FILE expired at 2026-10-17T11:00:00.0000000+00:00."]);
    }

    [Fact]
    public async Task Machine_WithoutAProject_AsksForOne()
    {
        UseClientCredentials();
        _http.Respond(HttpStatusCode.OK, """{"access_token":"cc-1","expires_in":3600}""");

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.WARNING_INTERACTION_NEEDED);
        _console.Lines.ShouldContain("! No project is set: set STORYTELLER_PROJECT=<organization>.<project> or pass -p to each command.");
    }

    [Fact]
    public async Task Machine_TargetingAnotherProjectThanItsKey_IsAWrongInput()
    {
        _environment[StorytellerVariables.API_KEY] = TestApiKeys.Create("acme", "billing", "3f2c");
        _environment[StorytellerVariables.PROJECT] = "acme.web";

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldContain("! The machine credentials in STORYTELLER_API_KEY belong to acme.billing, not acme.web.");
    }

    [Theory]
    [InlineData(null, HttpStatusCode.OK, ExitCodes.ERROR_WRONG_INPUT, "Set STORYTELLER_API_URL to the API base URL, for example https://<host>/api.")]
    [InlineData(ApiUrl, HttpStatusCode.Unauthorized, ExitCodes.WARNING_UNAUTHORIZED_ACCESS, "The token request to https://auth.example.com/oauth2/token was rejected.")]
    [InlineData(ApiUrl, HttpStatusCode.BadGateway, ExitCodes.ERROR_SERVICE_FAILURE, "The token request to https://auth.example.com/oauth2/token failed with 502.")]
    public async Task Machine_ThatCannotSignIn_SaysWhy(string? apiUrl, HttpStatusCode tokenStatus, int expectedExitCode, string message)
    {
        UseClientCredentials();
        _environment[StorytellerVariables.PROJECT] = "acme.billing";
        _http.Respond(tokenStatus, string.Empty);

        if (apiUrl is null)
        {
            _environment.Remove(StorytellerVariables.API_URL);
        }

        var exitCode = await Execute();

        exitCode.ShouldBe(expectedExitCode);
        _console.Lines.ShouldBe([$"! {message}"]);
    }

    private void UseClientCredentials()
    {
        _environment[StorytellerVariables.CLIENT_ID] = "client_01";
        _environment[StorytellerVariables.CLIENT_SECRET] = "client-secret";
        _environment[StorytellerVariables.TOKEN_URL] = TokenUrl;
    }

    // The strict mocks prove that a machine never starts a device code sign-in or calls the account API.
    private Task<int> Execute(bool openBrowser = false, MockFileSystem? fileSystem = null)
    {
        var settings = MachineSignInSettings.FromEnvironment(name => _environment.GetValueOrDefault(name));
        var machine = new MachineAuthenticationService(
            settings,
            fileSystem ?? new MockFileSystem(),
            new StubHttpClientFactory(_http),
            Mock.Of<IAuthenticationConfigurationResolver>(),
            new ManualTimeProvider(Start));
        var stored = Options.Create(new AccessDefaultOptions { OrganizationName = "house", ProjectName = "stored" });
        var command = new AccountCommand(_console.Object, _accessApi.Object, _authentication.Object, stored, machine, new CommandContext(stored, machine))
        {
            OpenBrowser = openBrowser,
        };

        return command.OnExecuteAsync();
    }
}
