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
using _42.Platform.Storyteller.Sdk.Credentials;
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
    private static readonly string JwtExchangeAnswer = $$"""{"access_token":"{{TestTokens.AccessToken(Start.AddMinutes(15))}}","token_type":"Bearer","expires_in":900}""";
    private static readonly string SubjectTokenPath = MockUnixSupport.Path(@"C:\ci\platform-token");

    private readonly RecordingConsole _console = new();
    private readonly Mock<IAccessApiClient> _accessApi = new(MockBehavior.Strict);
    private readonly Mock<IAuthenticationService> _authentication = new(MockBehavior.Strict);
    private readonly ScriptedHttpHandler _http = new();
    private readonly MockFileSystem _fileSystem = new();
    private readonly Dictionary<string, string> _environment = new() { [StorytellerVariables.ENDPOINT] = ApiUrl };

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
        _environment[TargetVariables.PROJECT] = "acme.billing";
        _environment[TargetVariables.VIEW] = "preview";
        _http.Respond(HttpStatusCode.OK, $$"""{"access_token":"{{TestTokens.AccessToken(Start.AddHours(1))}}","expires_in":3600}""");

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldContain("Signed in as machine client_01 with client credentials (STORYTELLER_CLIENT_ID, STORYTELLER_CLIENT_SECRET, STORYTELLER_TOKEN_URL).");
        _console.Lines.ShouldContain("Project acme.billing with preview view.");
        _console.Lines.ShouldContain("Token valid until 2026-10-17T13:00:00.0000000+00:00.");
        _http.Requests.ShouldHaveSingleItem().Uri.ShouldBe(new Uri(TokenUrl));
    }

    // The expiry is the exp of a JWT: an opaque token that sform got has none.
    [Fact]
    public async Task Machine_WithClientCredentials_AndAnOpaqueToken_SaysTheExpiryIsUnknown()
    {
        UseClientCredentials();
        _environment[TargetVariables.PROJECT] = "acme.billing";
        _http.Respond(HttpStatusCode.OK, """{"access_token":"cc-1","expires_in":3600}""");

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldBe(
        [
            "# Machine sign-in",
            "Signed in as machine client_01 with client credentials (STORYTELLER_CLIENT_ID, STORYTELLER_CLIENT_SECRET, STORYTELLER_TOKEN_URL).",
            "Project acme.billing with default view.",
            $"API {ApiUrl}",
            "Signed in; the expiry of the token is unknown.",
        ]);
    }

    [Fact]
    public async Task Machine_WithATokenFile_IsAnUnnamedMachine()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(MockUnixSupport.Path(@"C:\ci\token"), new MockFileData("opaque-token"));
        _environment[StorytellerVariables.TOKEN_FILE] = MockUnixSupport.Path(@"C:\ci\token");
        _environment[TargetVariables.PROJECT] = "acme.billing";

        var exitCode = await Execute(fileSystem: fileSystem);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldContain("Signed in as a machine with a token file (STORYTELLER_TOKEN_FILE).");
        _console.Lines.ShouldContain("The API checks the credentials on the first call.");
    }

    [Fact]
    public async Task Machine_Federated_WithAFile_ExchangesAndShowsTheMachineAndTheProject()
    {
        UseFederatedFile();
        _environment[TargetVariables.PROJECT] = "acme.billing";
        _http.Respond(HttpStatusCode.OK, JwtExchangeAnswer);

        var exitCode = await Execute(fileSystem: _fileSystem);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldBe(
        [
            "# Machine sign-in",
            $"Signed in as machine {FederatedMachineId} with workload identity federation (STORYTELLER_CLIENT_ID, STORYTELLER_TOKEN_FILE).",
            "Project acme.billing with default view.",
            $"API {ApiUrl}",
            "Token valid until 2026-10-17T12:15:00.0000000+00:00.",
        ]);
        _http.Requests.ShouldHaveSingleItem().Uri.ShouldBe(new Uri(TokenEndpoint));
    }

    [Fact]
    public async Task Machine_Federated_FromGitHubActions_AsksTheRunnerAndExchanges()
    {
        _environment[StorytellerVariables.CLIENT_ID] = FederatedMachineId;
        _environment[StorytellerVariables.ACTIONS_ID_TOKEN_REQUEST_URL] = GitHubRequestUrl;
        _environment[StorytellerVariables.ACTIONS_ID_TOKEN_REQUEST_TOKEN] = "github-request-token";
        _environment[TargetVariables.PROJECT] = "acme.billing";
        _http.Respond(HttpStatusCode.OK, """{"count":1,"value":"gh-jwt"}""")
            .Respond(HttpStatusCode.OK, JwtExchangeAnswer);

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
        $"The token request to {TokenEndpoint} was rejected with 400: invalid_request: The subject token was not accepted.")]
    [InlineData(
        HttpStatusCode.ServiceUnavailable,
        """{"error":"temporarily_unavailable"}""",
        ExitCodes.ERROR_SERVICE_FAILURE,
        $"The token request to {TokenEndpoint} failed with 503: temporarily_unavailable.")]
    [InlineData(
        HttpStatusCode.OK,
        "{}",
        ExitCodes.WARNING_UNAUTHORIZED_ACCESS,
        $"The token endpoint {TokenEndpoint} returned no bearer token.")]
    public async Task Machine_Federated_ThatCannotExchange_SaysWhy(HttpStatusCode status, string body, int expectedExitCode, string message)
    {
        UseFederatedFile();
        _environment[TargetVariables.PROJECT] = "acme.billing";
        _http.Respond(status, body);

        var exitCode = await Execute(fileSystem: _fileSystem);

        exitCode.ShouldBe(expectedExitCode);
        _console.Lines.ShouldBe([$"! {message}"]);
    }

    [Fact]
    public async Task Machine_Federated_WithAnUnreachableTokenEndpoint_IsAServiceFailure()
    {
        UseFederatedFile();
        _environment[TargetVariables.PROJECT] = "acme.billing";
        _http.Fail(new HttpRequestException("No such host is known."));

        var exitCode = await Execute(fileSystem: _fileSystem);

        exitCode.ShouldBe(ExitCodes.ERROR_SERVICE_FAILURE);
        _console.Lines.ShouldBe([$"! The token request to {TokenEndpoint} failed: No such host is known."]);
    }

    [Theory]
    [InlineData("file", "The platform token file {0} in STORYTELLER_TOKEN_FILE does not exist.")]
    [InlineData(StorytellerVariables.ENDPOINT, "Set STORYTELLER_ENDPOINT to the API base URL, for example https://<host>/api.")]
    public async Task Machine_Federated_WithoutTheEndpointOrTheFile_IsAWrongInputWithoutARequest(string missing, string message)
    {
        UseFederatedFile();
        _environment[TargetVariables.PROJECT] = "acme.billing";

        // The variable of the file stays: only the file is missing.
        if (missing == "file")
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

    // The deprecated name always means federation, so it needs the machine id; it is named as deprecated even then.
    [Fact]
    public async Task Machine_WithTheDeprecatedSubjectTokenFileAndNoClientId_IsAWrongInputThatNamesTheDeprecation()
    {
        _fileSystem.AddFile(SubjectTokenPath, new MockFileData("platform-token"));
        _environment[StorytellerVariables.SUBJECT_TOKEN_FILE] = SubjectTokenPath;
        _environment[TargetVariables.PROJECT] = "acme.billing";

        var exitCode = await Execute(fileSystem: _fileSystem);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldBe(
        [
            "! A federated machine needs STORYTELLER_CLIENT_ID, the machine id.",
            "! STORYTELLER_SUBJECT_TOKEN_FILE is deprecated; use STORYTELLER_TOKEN_FILE.",
        ]);
        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Machine_Federated_NeverUsesTheStoredUserSessionNorStartsADeviceCode()
    {
        UseFederatedFile();
        _environment[TargetVariables.PROJECT] = "acme.billing";
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
        _environment[TargetVariables.PROJECT] = "acme.billing";

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
        _environment[TargetVariables.PROJECT] = "acme.web";

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldContain("! The machine credentials in STORYTELLER_API_KEY belong to acme.billing, not acme.web.");
    }

    [Theory]
    [InlineData(null, HttpStatusCode.OK, ExitCodes.ERROR_WRONG_INPUT, "Set STORYTELLER_ENDPOINT to the API base URL, for example https://<host>/api.")]
    [InlineData(ApiUrl, HttpStatusCode.Unauthorized, ExitCodes.WARNING_UNAUTHORIZED_ACCESS, "The token request to https://auth.example.com/oauth2/token was rejected with 401.")]
    [InlineData(ApiUrl, HttpStatusCode.Forbidden, ExitCodes.WARNING_UNAUTHORIZED_ACCESS, "The token request to https://auth.example.com/oauth2/token was rejected with 403.")]
    [InlineData(ApiUrl, HttpStatusCode.BadGateway, ExitCodes.ERROR_SERVICE_FAILURE, "The token request to https://auth.example.com/oauth2/token failed with 502.")]
    public async Task Machine_ThatCannotSignIn_SaysWhy(string? apiUrl, HttpStatusCode tokenStatus, int expectedExitCode, string message)
    {
        UseClientCredentials();
        _environment[TargetVariables.PROJECT] = "acme.billing";
        _http.Respond(tokenStatus, string.Empty);

        if (apiUrl is null)
        {
            _environment.Remove(StorytellerVariables.ENDPOINT);
        }

        var exitCode = await Execute();

        exitCode.ShouldBe(expectedExitCode);
        _console.Lines.ShouldBe([$"! {message}"]);
    }

    [Fact]
    public async Task Machine_WithDeprecatedNames_NamesEachOneAfterTheSignIn()
    {
        _environment.Remove(StorytellerVariables.ENDPOINT);
        _environment[StorytellerVariables.API_URL] = ApiUrl;
        _fileSystem.AddFile(SubjectTokenPath, new MockFileData("platform-token"));
        _environment[StorytellerVariables.CLIENT_ID] = FederatedMachineId;
        _environment[StorytellerVariables.SUBJECT_TOKEN_FILE] = SubjectTokenPath;
        _environment[TargetVariables.PROJECT] = "acme.billing";
        _http.Respond(HttpStatusCode.OK, JwtExchangeAnswer);

        var exitCode = await Execute(fileSystem: _fileSystem);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldBe(
        [
            "# Machine sign-in",
            $"Signed in as machine {FederatedMachineId} with workload identity federation (STORYTELLER_CLIENT_ID, STORYTELLER_SUBJECT_TOKEN_FILE).",
            "Project acme.billing with default view.",
            $"API {ApiUrl}",
            "Token valid until 2026-10-17T12:15:00.0000000+00:00.",
            "! STORYTELLER_API_URL is deprecated; use STORYTELLER_ENDPOINT.",
            "! STORYTELLER_SUBJECT_TOKEN_FILE is deprecated; use STORYTELLER_TOKEN_FILE.",
        ]);
        _http.Requests.ShouldHaveSingleItem().Uri.ShouldBe(new Uri(TokenEndpoint));
    }

    [Fact]
    public async Task Machine_WithTheEndpointUnderBothNames_ThatDiffer_IsAWrongInput()
    {
        _environment[StorytellerVariables.API_URL] = "https://other.example.com/api";
        _environment[StorytellerVariables.API_KEY] = TestApiKeys.Create("acme", "billing", "3f2c");

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldBe(
        [
            "! STORYTELLER_ENDPOINT and STORYTELLER_API_URL are both set, to different values; set one of them.",
            "! STORYTELLER_API_URL is deprecated; use STORYTELLER_ENDPOINT.",
        ]);
    }

    private void UseFederatedFile()
    {
        _fileSystem.AddFile(SubjectTokenPath, new MockFileData("platform-token"));
        _environment[StorytellerVariables.CLIENT_ID] = FederatedMachineId;
        _environment[StorytellerVariables.TOKEN_FILE] = SubjectTokenPath;
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
        var machine = TestMachines.Create(_environment, fileSystem ?? new MockFileSystem(), _http, new ManualTimeProvider(Start));
        var stored = Options.Create(new AccessDefaultOptions { OrganizationName = "house", ProjectName = "stored" });
        var command = new AccountCommand(_console.Object, _accessApi.Object, _authentication.Object, stored, machine, new CommandContext(stored, machine))
        {
            OpenBrowser = openBrowser,
        };

        return command.OnExecuteAsync();
    }
}
