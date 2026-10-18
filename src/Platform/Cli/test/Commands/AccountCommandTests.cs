using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
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
    private const string FederatedMachineId = "6d0f2c4e-0000-4000-8000-000000000001";
    private const string TokenEndpoint = $"{ApiUrl}/v1/acme/billing/access/token";
    private const string GitHubRequestUrl = "https://pipelines.actions.githubusercontent.com/abc/idtoken?api-version=2.0";
    private const string ExchangeAnswer = """{"access_token":"st-1","token_type":"Bearer","expires_in":900}""";

    private static readonly DateTimeOffset Start = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly string SubjectTokenPath = MockUnixSupport.Path(@"C:\ci\platform-token");

    private readonly RecordingConsole _console = new();
    private readonly Mock<IAccessApiClient> _accessApi = new(MockBehavior.Strict);
    private readonly Mock<IAuthenticationService> _authentication = new(MockBehavior.Strict);
    private readonly ScriptedHttpHandler _http = new();
    private readonly MockFileSystem _fileSystem = new();
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
    public async Task Machine_Federated_FromGitHubActions_AsksTheRunnerAndExchanges()
    {
        _environment[StorytellerVariables.CLIENT_ID] = FederatedMachineId;
        _environment[GitHubActionsVariables.ID_TOKEN_REQUEST_URL] = GitHubRequestUrl;
        _environment[GitHubActionsVariables.ID_TOKEN_REQUEST_TOKEN] = "github-request-token";
        _environment[StorytellerVariables.PROJECT] = "acme.billing";
        _http.Respond(HttpStatusCode.OK, """{"count":1,"value":"gh-jwt"}""")
            .Respond(HttpStatusCode.OK, ExchangeAnswer);

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldBe(
        [
            "# Machine sign-in",
            $"Signed in as machine {FederatedMachineId} with workload identity federation (STORYTELLER_CLIENT_ID, ACTIONS_ID_TOKEN_REQUEST_URL, ACTIONS_ID_TOKEN_REQUEST_TOKEN).",
            "Project acme.billing with default view.",
            $"API {ApiUrl}",
            "Token valid until 2026-10-17T12:15:00.0000000+00:00.",
        ]);
        _http.Requests.Select(request => (request.Method, request.Uri.AbsoluteUri)).ShouldBe(
        [
            (HttpMethod.Get, $"{GitHubRequestUrl}&audience={Uri.EscapeDataString(TokenEndpoint)}"),
            (HttpMethod.Post, TokenEndpoint),
        ]);
    }

    [Fact]
    public async Task Machine_Federated_WithoutAProject_AsksForOneBeforeAnyRequest()
    {
        UseFederatedFile();

        var exitCode = await Execute(fileSystem: _fileSystem);

        exitCode.ShouldBe(ExitCodes.WARNING_INTERACTION_NEEDED);
        _console.Lines.ShouldBe(["! No project is set: set STORYTELLER_PROJECT=<organization>.<project> or pass -p to each command."]);
        _http.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(
        HttpStatusCode.BadRequest,
        """{"error":"invalid_request","error_description":"The subject token was not accepted."}""",
        ExitCodes.WARNING_UNAUTHORIZED_ACCESS,
        $"The token request to {TokenEndpoint} was rejected: invalid_request: The subject token was not accepted.")]
    [InlineData(
        HttpStatusCode.ServiceUnavailable,
        """{"error":"temporarily_unavailable"}""",
        ExitCodes.ERROR_SERVICE_FAILURE,
        $"The token request to {TokenEndpoint} failed with 503.")]
    public async Task Machine_Federated_ThatCannotExchange_SaysWhy(HttpStatusCode status, string body, int expectedExitCode, string message)
    {
        UseFederatedFile();
        _environment[StorytellerVariables.PROJECT] = "acme.billing";
        _http.Respond(status, body);

        var exitCode = await Execute(fileSystem: _fileSystem);

        exitCode.ShouldBe(expectedExitCode);
        _console.Lines.ShouldBe([$"! {message}"]);
    }

    [Fact]
    public async Task Machine_Federated_WithAnUnreachableTokenEndpoint_IsAServiceFailure()
    {
        UseFederatedFile();
        _environment[StorytellerVariables.PROJECT] = "acme.billing";
        _http.Fail(new HttpRequestException("No such host is known."));

        var exitCode = await Execute(fileSystem: _fileSystem);

        exitCode.ShouldBe(ExitCodes.ERROR_SERVICE_FAILURE);
        _console.Lines.ShouldBe([$"! The token endpoint {TokenEndpoint} could not be reached."]);
    }

    [Theory]
    [InlineData(StorytellerVariables.SUBJECT_TOKEN_FILE, "The platform token file {0} in STORYTELLER_SUBJECT_TOKEN_FILE does not exist.")]
    [InlineData(StorytellerVariables.CLIENT_ID, "A federated machine needs STORYTELLER_CLIENT_ID, the machine id.")]
    [InlineData(StorytellerVariables.API_URL, "Set STORYTELLER_API_URL to the API base URL, for example https://<host>/api.")]
    public async Task Machine_Federated_WithoutAVariableOrTheFile_IsAWrongInputWithoutARequest(string missing, string message)
    {
        UseFederatedFile();
        _environment[StorytellerVariables.PROJECT] = "acme.billing";

        // The variable of the file stays: only the file is missing.
        if (missing == StorytellerVariables.SUBJECT_TOKEN_FILE)
        {
            _fileSystem.RemoveFile(SubjectTokenPath);
        }
        else
        {
            _environment.Remove(missing);
        }

        var exitCode = await Execute(fileSystem: _fileSystem);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldBe([$"! {string.Format(message, SubjectTokenPath)}"]);
        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Machine_Federated_NeverUsesTheStoredUserSessionNorStartsADeviceCode()
    {
        UseFederatedFile();
        _environment[StorytellerVariables.PROJECT] = "acme.billing";
        _http.Respond(HttpStatusCode.OK, ExchangeAnswer);
        _authentication
            .Setup(authentication => authentication.GetSignedInUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SignedInUser("user_01", "ada@example.com", "Ada Lovelace"));

        var exitCode = await Execute(fileSystem: _fileSystem);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _authentication.Verify(authentication => authentication.GetSignedInUserAsync(It.IsAny<CancellationToken>()), Times.Never);
        _authentication.VerifyNoOtherCalls();
        _accessApi.VerifyNoOtherCalls();
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

    private void UseFederatedFile()
    {
        _fileSystem.AddFile(SubjectTokenPath, new MockFileData("platform-token"));
        _environment[StorytellerVariables.CLIENT_ID] = FederatedMachineId;
        _environment[StorytellerVariables.SUBJECT_TOKEN_FILE] = SubjectTokenPath;
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
