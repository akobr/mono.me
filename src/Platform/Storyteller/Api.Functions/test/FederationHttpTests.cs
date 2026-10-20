using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class FederationHttpTests
{
    private const string Org = "org1";
    private const string Project = "proj1";
    private const string MachineId = "3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2d";
    private const string Audience = "urn:storyteller:2s.platform:api";
    private const string GitHubIssuer = "https://token.actions.githubusercontent.com";
    private const string FormContentType = "application/x-www-form-urlencoded";
    private const string TokenUrl = "https://api.example/api/v1/org1/proj1/access/token";

    private static readonly FederatedIssuer SharedGitHub = new()
    {
        Name = "github",
        Issuer = GitHubIssuer,
        RequiredClaims = new Dictionary<string, IReadOnlyList<string>> { ["repository_owner_id"] = ["123456"] },
    };

    private readonly FederationAccessService _access = new();
    private readonly InMemoryFederatedIssuerStore _issuers = new();
    private readonly RecordingTokenExchange _exchange = new();
    private readonly FederationOptions _options = new() { Issuer = "urn:storyteller:2s.platform", Audience = Audience };

    private static bool DevAuth => Security.HttpRequestDataExtensions.IsDevelopmentAuthentication;

    private FederationHttp Http => new(_access, _issuers, _exchange, Options.Create(_options), FunctionTestDoubles.ApiJsonOptions, NullLogger<FederationHttp>.Instance);

    // Token endpoint: form and parameters
    [Fact]
    public async Task Token_ValidForm_IsExchangedAndAnsweredWithSnakeCaseAndNoStore()
    {
        var response = await Http.PostMachineToken(TokenRequest(Form(TokenParameters())), Org, Project);

        var (status, body) = await ReadAsync(response);
        status.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("access_token").GetString().ShouldBe("storyteller-token");
        body.GetProperty("issued_token_type").GetString().ShouldBe(OAuthTokenExchange.TokenTypes.AccessToken);
        body.GetProperty("token_type").GetString().ShouldBe("Bearer");
        body.GetProperty("expires_in").GetInt32().ShouldBe(900);
        body.GetProperty("scope").GetString().ShouldBe("Default.Read Annotation.Read");
        body.EnumerateObject().Select(property => property.Name).ShouldBe(["access_token", "issued_token_type", "token_type", "expires_in", "scope"], ignoreOrder: true);
        Header(response, "Content-Type").ShouldBe("application/json; charset=utf-8");
        Header(response, "Cache-Control").ShouldBe("no-store");
        Header(response, "Pragma").ShouldBe("no-cache");
        response.Headers.Contains("Retry-After").ShouldBeFalse();

        var request = _exchange.Requests.ShouldHaveSingleItem();
        request.Organization.ShouldBe(Org);
        request.Project.ShouldBe(Project);
        request.MachineId.ShouldBe(MachineId);
        request.SubjectToken.ShouldBe("eyJhbGciOiJSUzI1NiJ9.eyJpc3MiOiJ4In0.c2ln");
        request.SubjectTokenType.ShouldBe(OAuthTokenExchange.TokenTypes.Jwt);
        request.Scopes.ShouldBeNull();
        request.RequestedTokenType.ShouldBe(OAuthTokenExchange.TokenTypes.AccessToken);
    }

    [Fact]
    public async Task Token_ScopeAndRequestedTokenType_AreHandedOver()
    {
        var body = Form(TokenParameters(("requested_token_type", OAuthTokenExchange.TokenTypes.Jwt))) + "&scope=Default.Read+Annotation.Read";

        var response = await Http.PostMachineToken(TokenRequest(body), Org, Project);

        (await ReadAsync(response)).Status.ShouldBe(HttpStatusCode.OK);
        var request = _exchange.Requests.ShouldHaveSingleItem();
        request.Scopes.ShouldBe(["Default.Read", "Annotation.Read"]);
        request.RequestedTokenType.ShouldBe(OAuthTokenExchange.TokenTypes.Jwt);
    }

    [Theory]
    [InlineData("application/x-www-form-urlencoded; charset=utf-8")]
    [InlineData("Application/X-WWW-Form-Urlencoded")]
    public async Task Token_FormContentTypeWithParameters_IsAccepted(string contentType)
    {
        var response = await Http.PostMachineToken(TokenRequest(Form(TokenParameters()), contentType), Org, Project);

        (await ReadAsync(response)).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("application/json")]
    [InlineData("multipart/form-data; boundary=x")]
    public async Task Token_OtherContentType_IsInvalidRequest(string? contentType)
    {
        var response = await Http.PostMachineToken(TokenRequest(Form(TokenParameters()), contentType), Org, Project);

        await ShouldBeOAuthErrorAsync(response, "invalid_request", "The content type must be application/x-www-form-urlencoded.");
        _exchange.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Token_BodyOverTheLimit_IsInvalidRequestWithoutParsing()
    {
        var body = Form(TokenParameters(("padding", new string('a', FederationHttp.MaxTokenRequestBytes))));

        var response = await Http.PostMachineToken(TokenRequest(body), Org, Project);

        await ShouldBeOAuthErrorAsync(response, "invalid_request", "The request body exceeds 32768 bytes.");
        _exchange.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Token_BodyAtTheLimit_IsParsed()
    {
        var form = Form(TokenParameters()) + "&padding=";
        var body = form + new string('a', FederationHttp.MaxTokenRequestBytes - form.Length);

        var response = await Http.PostMachineToken(TokenRequest(body), Org, Project);

        (await ReadAsync(response)).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("client_id")]
    [InlineData("subject_token")]
    [InlineData("grant_type")]
    public async Task Token_ParameterGivenTwice_IsInvalidRequest(string name)
    {
        var body = Form(TokenParameters()) + $"&{name}=again";

        var response = await Http.PostMachineToken(TokenRequest(body), Org, Project);

        await ShouldBeOAuthErrorAsync(response, "invalid_request", $"The parameter {name} is given more than once.");
        _exchange.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Token_UnknownParameterGivenTwice_IsInvalidRequestWithoutEchoingIt()
    {
        var body = Form(TokenParameters(("custom", "1"), ("custom", "2")));

        var response = await Http.PostMachineToken(TokenRequest(body), Org, Project);

        await ShouldBeOAuthErrorAsync(response, "invalid_request", "A parameter is given more than once.");
    }

    [Fact]
    public async Task Token_RepeatedAudienceAndResourceOfThisApi_AreAccepted()
    {
        var body = Form(TokenParameters(("audience", Audience), ("audience", Audience), ("resource", Audience)));

        var response = await Http.PostMachineToken(TokenRequest(body), Org, Project);

        (await ReadAsync(response)).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("audience", "https://other.example")]
    [InlineData("resource", "urn:storyteller:2s.platform:API")]
    public async Task Token_AnotherAudienceOrResource_IsInvalidTarget(string name, string value)
    {
        var body = Form(TokenParameters(("audience", Audience), (name, value)));

        var response = await Http.PostMachineToken(TokenRequest(body), Org, Project);

        await ShouldBeOAuthErrorAsync(response, "invalid_target", "The audience or resource is not this API.");
        _exchange.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Token_EmptyValues_CountAsAbsent()
    {
        // An empty duplicate is not a second value, and an empty scope or requested type falls back to the defaults.
        var body = Form(TokenParameters()) + "&client_id=&scope=&requested_token_type=&audience=";

        var response = await Http.PostMachineToken(TokenRequest(body), Org, Project);

        (await ReadAsync(response)).Status.ShouldBe(HttpStatusCode.OK);
        var request = _exchange.Requests.ShouldHaveSingleItem();
        request.MachineId.ShouldBe(MachineId);
        request.Scopes.ShouldBeNull();
        request.RequestedTokenType.ShouldBe(OAuthTokenExchange.TokenTypes.AccessToken);
    }

    [Theory]
    [InlineData("grant_type")]
    [InlineData("subject_token")]
    [InlineData("subject_token_type")]
    [InlineData("client_id")]
    public async Task Token_MissingOrEmptyRequiredParameter_IsInvalidRequest(string name)
    {
        var parameters = TokenParameters().Select(parameter => parameter.Name == name ? (parameter.Name, string.Empty) : parameter).ToArray();

        var response = await Http.PostMachineToken(TokenRequest(Form(parameters)), Org, Project);

        await ShouldBeOAuthErrorAsync(response, "invalid_request", $"The parameter {name} is missing.");
        _exchange.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Token_OtherGrantType_IsUnsupportedGrantType()
    {
        var parameters = TokenParameters().Select(parameter => parameter.Name == "grant_type" ? (parameter.Name, "client_credentials") : parameter).ToArray();

        var response = await Http.PostMachineToken(TokenRequest(Form(parameters)), Org, Project);

        await ShouldBeOAuthErrorAsync(response, "unsupported_grant_type", "Only the token exchange grant type is supported.");
    }

    [Theory]
    [InlineData(OAuthTokenExchange.TokenTypes.Jwt)]
    [InlineData(OAuthTokenExchange.TokenTypes.IdToken)]
    [InlineData(OAuthTokenExchange.TokenTypes.AccessToken)]
    public async Task Token_SupportedSubjectTokenTypes_AreAccepted(string type)
    {
        var parameters = TokenParameters().Select(parameter => parameter.Name == "subject_token_type" ? (parameter.Name, type) : parameter).ToArray();

        var response = await Http.PostMachineToken(TokenRequest(Form(parameters)), Org, Project);

        (await ReadAsync(response)).Status.ShouldBe(HttpStatusCode.OK);
        _exchange.Requests.ShouldHaveSingleItem().SubjectTokenType.ShouldBe(type);
    }

    [Fact]
    public async Task Token_OtherSubjectTokenType_IsInvalidRequest()
    {
        var parameters = TokenParameters().Select(parameter => parameter.Name == "subject_token_type" ? (parameter.Name, "urn:ietf:params:oauth:token-type:saml2") : parameter).ToArray();

        var response = await Http.PostMachineToken(TokenRequest(Form(parameters)), Org, Project);

        await ShouldBeOAuthErrorAsync(response, "invalid_request", "The subject token type is not supported.");
    }

    [Theory]
    [InlineData("actor_token")]
    [InlineData("actor_token_type")]
    public async Task Token_ActorToken_IsInvalidRequest(string name)
    {
        var response = await Http.PostMachineToken(TokenRequest(Form(TokenParameters((name, "x")))), Org, Project);

        await ShouldBeOAuthErrorAsync(response, "invalid_request", "Actor tokens are not supported.");
    }

    [Fact]
    public async Task Token_OtherRequestedTokenType_IsInvalidRequest()
    {
        var body = Form(TokenParameters(("requested_token_type", OAuthTokenExchange.TokenTypes.IdToken)));

        var response = await Http.PostMachineToken(TokenRequest(body), Org, Project);

        await ShouldBeOAuthErrorAsync(response, "invalid_request", "The requested token type is not supported.");
    }

    [Theory]
    [InlineData("Default.Read  Annotation.Read")]
    [InlineData(" Default.Read")]
    [InlineData("Default.Read\"")]
    [InlineData("Default\\Read")]
    [InlineData("Default.Read\tAnnotation.Read")]
    public async Task Token_MalformedScope_IsInvalidScope(string scope)
    {
        var response = await Http.PostMachineToken(TokenRequest(Form(TokenParameters(("scope", scope)))), Org, Project);

        await ShouldBeOAuthErrorAsync(response, "invalid_scope", "The parameter scope is malformed.");
        _exchange.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Token_RequestWithAuthorization_IsInvalidRequest()
    {
        var request = TokenRequest(Form(TokenParameters()), headers: new Dictionary<string, string> { ["Authorization"] = "Bearer eyJ.x.y" });

        var response = await Http.PostMachineToken(request, Org, Project);

        await ShouldBeOAuthErrorAsync(response, "invalid_request", "The token request must not carry an Authorization header.");
    }

    // Token endpoint: results of the exchange
    [Fact]
    public async Task Token_RefusedSubjectToken_IsTheOpaqueInvalidRequest()
    {
        _exchange.Result = FederatedTokenExchangeResult.NotAccepted();

        var response = await Http.PostMachineToken(TokenRequest(Form(TokenParameters())), Org, Project);

        await ShouldBeOAuthErrorAsync(response, "invalid_request", "The subject token is not accepted for this machine.");
        Header(response, "Cache-Control").ShouldBe("no-store");
        Header(response, "Pragma").ShouldBe("no-cache");
    }

    [Fact]
    public async Task Token_ScopeBeyondTheMachine_IsInvalidScope()
    {
        _exchange.Result = FederatedTokenExchangeResult.Failure(OAuthTokenExchange.Errors.InvalidScope, "The requested scope exceeds the scopes of the machine.");

        var response = await Http.PostMachineToken(TokenRequest(Form(TokenParameters(("scope", "Configuration.Secrets")))), Org, Project);

        await ShouldBeOAuthErrorAsync(response, "invalid_scope", "The requested scope exceeds the scopes of the machine.");
    }

    [Fact]
    public async Task Token_KeysUnavailable_IsServiceUnavailableWithRetryAfter()
    {
        _exchange.Result = FederatedTokenExchangeResult.Unavailable();

        var response = await Http.PostMachineToken(TokenRequest(Form(TokenParameters())), Org, Project);

        var (status, body) = await ReadAsync(response);
        status.ShouldBe(HttpStatusCode.ServiceUnavailable);
        body.GetProperty("error").GetString().ShouldBe("temporarily_unavailable");
        body.GetProperty("error_description").GetString().ShouldBe("The keys of the trusted issuer could not be retrieved.");
        Header(response, "Retry-After").ShouldBe("5");
        Header(response, "Content-Type").ShouldBe("application/json; charset=utf-8");
        Header(response, "Cache-Control").ShouldBe("no-store");
    }

    // Trusted issuers
    [Fact]
    public async Task GetIssuers_ReturnsTheIssuersOfTheProject()
    {
        await _issuers.SetAsync(Org, Project, GitHub());
        await _issuers.SetAsync(Org, "other", GitHub("elsewhere"));

        var result = await Http.GetFederatedIssuers(UserRequest(), Org, Project);

        var issuers = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeAssignableTo<IReadOnlyList<FederatedIssuer>>();
        issuers.ShouldNotBeNull();
        issuers.Select(issuer => issuer.Name).ShouldBe(["github"]);
    }

    [Fact]
    public async Task GetIssuer_KnownAndUnknown()
    {
        await _issuers.SetAsync(Org, Project, GitHub());

        (await Http.GetFederatedIssuer(UserRequest(), Org, Project, "github")).ShouldBeOfType<OkObjectResult>().Value.ShouldBe(GitHub());
        (await Http.GetFederatedIssuer(UserRequest(), Org, Project, "gitlab")).ShouldBeOfType<NotFoundResult>();
        (await Http.GetFederatedIssuer(UserRequest(), Org, Project, "Not_A_Name")).ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task PutIssuer_CreatesIt_TheRouteNameWins()
    {
        var result = await Http.PutFederatedIssuer(IssuerRequest(GitHub("from-body")), Org, Project, "github");

        ShouldBeIssuer(result.ShouldBeOfType<OkObjectResult>().Value, GitHub());
        ShouldBeIssuer(await _issuers.GetAsync(Org, Project, "github"), GitHub());
        (await _issuers.GetAsync(Org, Project, "from-body")).ShouldBeNull();
        _access.Calls.ShouldContain($"using:{Org}/{Project}/github");
    }

    [Fact]
    public async Task PutIssuer_NotValid_IsBadRequestAndNotStored()
    {
        var issuer = GitHub() with { Issuer = "http://token.actions.githubusercontent.com" };

        var result = await Http.PutFederatedIssuer(IssuerRequest(issuer), Org, Project, "github");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.ErrorCode.ShouldBe(ErrorCodes.InvalidFederation);
        error.Message.ShouldContain("cannot be discovered");
        (await _issuers.ListAsync(Org, Project)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Upper")]
    [InlineData("-github")]
    public async Task PutIssuer_InvalidRouteName_IsBadRequest(string name)
    {
        var result = await Http.PutFederatedIssuer(IssuerRequest(GitHub()), Org, Project, name);

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.InvalidFederation);
    }

    [Fact]
    public async Task PutIssuer_WithoutABody_IsBadRequest()
    {
        var result = await Http.PutFederatedIssuer(IssuerRequest(null), Org, Project, "github");

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().Message.ShouldBe("The issuer is required.");
    }

    // The worker's [FromBody] binding fails these bodies with an exception before the function runs (500); the
    // handler reads its body itself, so they reach the validation.
    [Theory]
    [InlineData("")]
    [InlineData(" \r\n")]
    [InlineData("null")]
    public async Task PutIssuer_WithAnEmptyOrNullBody_IsBadRequest(string body)
    {
        var result = await Http.PutFederatedIssuer(RawRequest(body), Org, Project, "github");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe("The issuer is required.");
        error.ErrorCode.ShouldBe(ErrorCodes.InvalidFederation);
    }

    [Theory]
    [InlineData("{", "The request body is not a trusted issuer")]
    [InlineData("[1, 2]", "The request body is not a trusted issuer")]
    [InlineData("{ \"Name\": \"github\", \"Issuer\": \"https://token.actions.githubusercontent.com\", \"KeySource\": \"Telepathy\" }", "The request body is not a trusted issuer (at $.KeySource).")]
    public async Task PutIssuer_WithABodyThatIsNotAnIssuer_IsBadRequestAndNotStored(string body, string message)
    {
        var result = await Http.PutFederatedIssuer(RawRequest(body), Org, Project, "github");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldStartWith(message);
        error.ErrorCode.ShouldBe(ErrorCodes.InvalidFederation);
        (await _issuers.ListAsync(Org, Project)).ShouldBeEmpty();
    }

    [Fact]
    public async Task PutIssuer_ReadsTheApiJson_WithEnumNamesAndAnyCase()
    {
        const string body = """
            { "issuer": "https://token.actions.githubusercontent.com", "KeySource": "Discovery",
              "RequiredClaims": { "repository_owner_id": [ "123456" ] }, }
            """;

        var result = await Http.PutFederatedIssuer(RawRequest(body), Org, Project, "github");

        ShouldBeIssuer(result.ShouldBeOfType<OkObjectResult>().Value, GitHub());
    }

    [Fact]
    public async Task PutIssuer_ChangingTheIssuerOfAnExistingOne_IsBadRequest()
    {
        await _issuers.SetAsync(Org, Project, GitHub());
        var changed = GitHub() with { Issuer = $"{GitHubIssuer}/acme" };

        var result = await Http.PutFederatedIssuer(IssuerRequest(changed), Org, Project, "github");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe(FederationValidation.IssuerImmutableMessage);
        error.ErrorCode.ShouldBe(ErrorCodes.InvalidFederation);
        (await _issuers.GetAsync(Org, Project, "github")).ShouldBe(GitHub());
    }

    [Fact]
    public async Task PutIssuer_ExistingOne_ChangesEverythingButTheIssuer()
    {
        await _issuers.SetAsync(Org, Project, GitHub());
        _access.Add(Org, Project, FederatedMachine(Rule()));
        var changed = GitHub() with { Description = "CI", MaxTokenLifetimeSeconds = 600 };

        var result = await Http.PutFederatedIssuer(IssuerRequest(changed), Org, Project, "github");

        ShouldBeIssuer(result.ShouldBeOfType<OkObjectResult>().Value, changed);
        ShouldBeIssuer(await _issuers.GetAsync(Org, Project, "github"), changed);

        // Rules of the issuer itself are no reason for a conflict; only a re-created name is.
        _access.Calls.ShouldNotContain(call => call.StartsWith("using:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PutIssuer_RecreatingANameRulesStillReference_IsConflict()
    {
        _access.Add(Org, Project, FederatedMachine(Rule()));

        var result = await Http.PutFederatedIssuer(IssuerRequest(GitHub()), Org, Project, "github");

        var conflict = result.ShouldBeOfType<ObjectResult>();
        conflict.StatusCode.ShouldBe((int)HttpStatusCode.Conflict);
        var error = conflict.Value.ShouldBeOfType<ErrorResponse>();
        error.ErrorCode.ShouldBe(ErrorCodes.FederatedIssuerInUse);
        error.Message.ShouldContain(MachineId);
        error.Message.ShouldNotContain("and maybe more");
        (await _issuers.ListAsync(Org, Project)).ShouldBeEmpty();
    }

    [Fact]
    public async Task PutIssuer_Conflict_NamesAtMostTenMachines()
    {
        for (var index = 0; index < 12; index++)
        {
            _access.Add(Org, Project, FederatedMachine(Rule()) with { Id = $"00000000-0000-0000-0000-0000000000{index:D2}" });
        }

        var result = await Http.PutFederatedIssuer(IssuerRequest(GitHub()), Org, Project, "github");

        var error = result.ShouldBeOfType<ObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.Split("00000000-0000-0000-0000-").Length.ShouldBe(FederationHttp.MaxMachinesNamedInConflict + 1);
        error.Message.ShouldContain("and maybe more");
    }

    [Fact]
    public async Task PutIssuer_TheIssuerNumber21_IsBadRequest()
    {
        for (var index = 0; index < FederationValidation.MaxIssuersPerProject; index++)
        {
            await _issuers.SetAsync(Org, Project, Custom($"issuer-{index:D2}"));
        }

        var result = await Http.PutFederatedIssuer(IssuerRequest(GitHub()), Org, Project, "github");

        var badRequest = result.ShouldBeOfType<ObjectResult>();
        badRequest.StatusCode.ShouldBe((int)HttpStatusCode.BadRequest);
        var error = badRequest.Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe(FederationValidation.TooManyIssuersMessage);
        error.ErrorCode.ShouldBe(ErrorCodes.InvalidFederation);
        (await _issuers.ListAsync(Org, Project)).Count.ShouldBe(FederationValidation.MaxIssuersPerProject);
    }

    [Fact]
    public async Task DeleteIssuer_ExistingAndUnknown()
    {
        await _issuers.SetAsync(Org, Project, GitHub());

        (await Http.DeleteFederatedIssuer(UserRequest(), Org, Project, "github")).ShouldBeOfType<OkResult>();
        (await Http.DeleteFederatedIssuer(UserRequest(), Org, Project, "github")).ShouldBeOfType<NotFoundResult>();
        (await _issuers.ListAsync(Org, Project)).ShouldBeEmpty();
    }

    [Fact]
    public async Task DeleteIssuer_WhileRulesReferenceIt_IsAllowed()
    {
        await _issuers.SetAsync(Org, Project, GitHub());
        _access.Add(Org, Project, FederatedMachine(Rule()));

        (await Http.DeleteFederatedIssuer(UserRequest(), Org, Project, "github")).ShouldBeOfType<OkResult>();
    }

    [Theory]
    [InlineData(AccountRole.Reader, false, false)]
    [InlineData(AccountRole.Contributor, true, false)]
    [InlineData(AccountRole.ContributorWithSecrets, true, false)]
    [InlineData(AccountRole.Administrator, true, true)]
    [InlineData(AccountRole.Owner, true, true)]
    public async Task IssuerRoutes_ReadByContributors_WrittenByAdministrators(AccountRole role, bool canRead, bool canWrite)
    {
        await _issuers.SetAsync(Org, Project, GitHub());
        _access.Role = role;

        await ShouldBeAllowedAsync(() => Http.GetFederatedIssuers(UserRequest(), Org, Project), canRead);
        await ShouldBeAllowedAsync(() => Http.GetFederatedIssuer(UserRequest(), Org, Project, "github"), canRead);
        await ShouldBeAllowedAsync(() => Http.PutFederatedIssuer(IssuerRequest(GitHub() with { Description = "x" }), Org, Project, "github"), canWrite);
        await ShouldBeAllowedAsync(() => Http.DeleteFederatedIssuer(UserRequest(), Org, Project, "github"), canWrite);
    }

    [Fact]
    public async Task IssuerRoutes_MachineCredentials_AreUnauthorized()
    {
        if (DevAuth)
        {
            // Debug builds skip the scope check (DEV_AUTH).
            return;
        }

        await Should.ThrowAsync<SecurityTokenException>(() => Http.GetFederatedIssuers(MachineRequest(), Org, Project));
        await Should.ThrowAsync<SecurityTokenException>(() => Http.PutFederatedIssuer(MachineRequest(), Org, Project, "github"));
        await Should.ThrowAsync<SecurityTokenException>(() => Http.PutMachineFederationRules(MachineRequest(), Org, Project, MachineId));
    }

    // Federation rules
    [Fact]
    public async Task PutRules_ReplacesTheRulesAndReturnsTheTokenEndpoint()
    {
        await _issuers.SetAsync(Org, Project, GitHub());
        _access.Add(Org, Project, FederatedMachine(Rule()));
        var rules = new List<FederationRule> { Rule("repo:acme/app:environment:prod"), Rule("repo:acme/app:ref:refs/heads/*") };

        var result = await Http.PutMachineFederationRules(RulesRequest(rules), Org, Project, MachineId);

        var machine = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<MachineAccess>();
        machine.FederationRules.ShouldBe(rules);
        machine.TokenEndpoint.ShouldBe("https://api.example/api/v1/org1/proj1/access/token");
        _access.Find(Org, Project, MachineId)!.FederationRules.ShouldBe(rules);
    }

    [Fact]
    public async Task PutRules_RuleOfAnUnknownIssuer_IsBadRequestAndNotStored()
    {
        _access.Add(Org, Project, FederatedMachine(Rule()));

        var result = await Http.PutMachineFederationRules(RulesRequest([Rule() with { Issuer = "gitlab" }]), Org, Project, MachineId);

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe("Rule 1: the trusted issuer 'gitlab' doesn't exist in the project.");
        error.ErrorCode.ShouldBe(ErrorCodes.InvalidFederation);
        _access.Calls.ShouldNotContain(call => call.StartsWith("rules:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PutRules_UnsafeRule_IsBadRequest()
    {
        await _issuers.SetAsync(Org, Project, GitHub() with { RequiredClaims = null });
        _access.Add(Org, Project, FederatedMachine(Rule()));

        var result = await Http.PutMachineFederationRules(RulesRequest([Rule()]), Org, Project, MachineId);

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().Message.ShouldStartWith("Rule 1:");
    }

    [Fact]
    public async Task PutRules_NoRules_IsBadRequest()
    {
        await _issuers.SetAsync(Org, Project, GitHub());

        (await Http.PutMachineFederationRules(RulesRequest(null), Org, Project, MachineId)).ShouldBeOfType<BadRequestObjectResult>();
        (await Http.PutMachineFederationRules(RulesRequest([]), Org, Project, MachineId)).ShouldBeOfType<BadRequestObjectResult>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    public async Task PutRules_WithAnEmptyOrNullBody_IsBadRequest(string body)
    {
        await _issuers.SetAsync(Org, Project, GitHub());
        _access.Add(Org, Project, FederatedMachine(Rule()));

        var result = await Http.PutMachineFederationRules(RawRequest(body), Org, Project, MachineId);

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe("A Federated machine needs at least one federation rule.");
        error.ErrorCode.ShouldBe(ErrorCodes.InvalidFederation);
        _access.Calls.ShouldNotContain(call => call.StartsWith("rules:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[ { \"Issuer\": 42 } ]")]
    public async Task PutRules_WithABodyThatIsNotAListOfRules_IsBadRequest(string body)
    {
        await _issuers.SetAsync(Org, Project, GitHub());
        _access.Add(Org, Project, FederatedMachine(Rule()));

        var result = await Http.PutMachineFederationRules(RawRequest(body), Org, Project, MachineId);

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldStartWith("The request body is not a list of federation rules");
        error.ErrorCode.ShouldBe(ErrorCodes.InvalidFederation);
        _access.Calls.ShouldNotContain(call => call.StartsWith("rules:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PutRules_UnknownMachine_IsNotFound()
    {
        await _issuers.SetAsync(Org, Project, GitHub());

        var result = await Http.PutMachineFederationRules(RulesRequest([Rule()]), Org, Project, MachineId);

        result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PutRules_MachineOfAnotherKind_IsBadRequest()
    {
        await _issuers.SetAsync(Org, Project, GitHub());
        _access.Add(Org, Project, FederatedMachine() with { CredentialKind = MachineCredentialKind.ApiKey, FederationRules = null });

        var result = await Http.PutMachineFederationRules(RulesRequest([Rule()]), Org, Project, MachineId);

        var badRequest = result.ShouldBeOfType<ObjectResult>();
        badRequest.StatusCode.ShouldBe((int)HttpStatusCode.BadRequest);
        badRequest.Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.MachineNotFederated);
    }

    [Fact]
    public async Task PutRules_InvalidMachineId_IsBadRequest()
    {
        var result = await Http.PutMachineFederationRules(RulesRequest([Rule()]), Org, Project, "../other");

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().Message.ShouldBe(Definitions.Errors.InvalidMachineId);
    }

    [Theory]
    [InlineData(AccountRole.Reader, false)]
    [InlineData(AccountRole.Contributor, true)]
    public async Task PutRules_IsForContributors(AccountRole role, bool allowed)
    {
        await _issuers.SetAsync(Org, Project, GitHub());
        _access.Add(Org, Project, FederatedMachine(Rule()));
        _access.Role = role;

        await ShouldBeAllowedAsync(() => Http.PutMachineFederationRules(RulesRequest([Rule()]), Org, Project, MachineId), allowed);
    }

    // The rules decide which workload gets the tokens of the machine, so its management scopes need an Administrator.
    [Theory]
    [InlineData(AccountRole.Contributor, false)]
    [InlineData(AccountRole.Administrator, true)]
    public async Task PutRules_OfAMachineWithManagementScopes_IsForAdministrators(AccountRole role, bool allowed)
    {
        await _issuers.SetAsync(Org, Project, GitHub());
        _access.Add(Org, Project, FederatedMachine(Rule()) with { ManagementScopes = [MachineManagementScope.ConfigurationTransfer] });
        _access.Role = role;

        await ShouldBeAllowedAsync(() => Http.PutMachineFederationRules(RulesRequest([Rule("repo:acme/app:environment:prod")]), Org, Project, MachineId), allowed);

        _access.Find(Org, Project, MachineId)!.FederationRules!.Single().Subject
            .ShouldBe(allowed || DevAuth ? "repo:acme/app:environment:prod" : Rule().Subject);
        _access.Find(Org, Project, MachineId)!.ManagementScopes.ShouldBe([MachineManagementScope.ConfigurationTransfer]);
    }

    // Token endpoint URL
    [Fact]
    public void TokenEndpoint_WithoutPublicBaseUrl_IsTheRequestSchemeAndHost_NeverAForwardedHost()
    {
        var request = UserRequest("https://api.example:8443/api/v1/org1/proj1/access/machines", new Dictionary<string, string> { ["X-Forwarded-Host"] = "evil.example" });

        FederationHttp.GetTokenEndpoint(request, new FederationOptions(), Org, Project)
            .ShouldBe("https://api.example:8443/api/v1/org1/proj1/access/token");
    }

    [Theory]
    [InlineData("https://storyteller.example/api")]
    [InlineData("https://storyteller.example/api/")]
    public void TokenEndpoint_WithPublicBaseUrl_UsesIt(string publicBaseUrl)
    {
        FederationHttp.GetTokenEndpoint(UserRequest(), new FederationOptions { PublicBaseUrl = publicBaseUrl }, Org, Project)
            .ShouldBe("https://storyteller.example/api/v1/org1/proj1/access/token");
    }

    [Fact]
    public async Task MachineRoutes_SetTheTokenEndpointOfFederatedMachinesOnly()
    {
        var apiKeyMachine = FederatedMachine() with { Id = "6f1c2d3e-4a5b-4c6d-8e7f-901a2b3c4d5e", CredentialKind = MachineCredentialKind.ApiKey, FederationRules = null };
        _access.Add(Org, Project, FederatedMachine(Rule()));
        _access.Add(Org, Project, apiKeyMachine);
        _access.Created = FederatedMachine(Rule());
        var http = new AccessHttp(_access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance, federationOptions: Options.Create(_options));
        const string expected = "https://api.example/api/v1/org1/proj1/access/token";
        var create = FunctionTestDoubles.ToJsonBody(new MachineAccessCreate { Organization = Org, Project = Project, Scope = MachineAccessScope.DefaultRead });

        var created = await http.PostMachines(UserRequest(headers: FunctionTestDoubles.JsonContentType(), method: "POST", body: create), Org, Project);
        var federated = await http.GetMachine(UserRequest(), Org, Project, MachineId);
        var apiKey = await http.GetMachine(UserRequest(), Org, Project, apiKeyMachine.Id);
        var list = await http.GetMachines(UserRequest(), Org, Project);

        created.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<MachineAccess>().TokenEndpoint.ShouldBe(expected);
        federated.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<MachineAccess>().TokenEndpoint.ShouldBe(expected);
        apiKey.ShouldBeOfType<OkObjectResult>().Value.ShouldBeSameAs(apiKeyMachine);
        var machines = list.ShouldBeOfType<OkObjectResult>().Value.ShouldBeAssignableTo<IEnumerable<MachineAccess>>();
        machines.ShouldNotBeNull();
        machines.Select(machine => (machine.CredentialKind, machine.TokenEndpoint))
            .ShouldBe([(MachineCredentialKind.Federated, expected), (MachineCredentialKind.ApiKey, null)], ignoreOrder: true);
    }

    private static (string Name, string Value)[] TokenParameters(params (string Name, string Value)[] extra)
    {
        return
        [
            ("grant_type", OAuthTokenExchange.GrantType),
            ("subject_token", "eyJhbGciOiJSUzI1NiJ9.eyJpc3MiOiJ4In0.c2ln"),
            ("subject_token_type", OAuthTokenExchange.TokenTypes.Jwt),
            ("client_id", MachineId),
            .. extra,
        ];
    }

    private static string Form(IEnumerable<(string Name, string Value)> parameters)
    {
        return string.Join('&', parameters.Select(parameter => $"{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(parameter.Value)}"));
    }

    private static TestHttpRequestData TokenRequest(string body, string? contentType = FormContentType, IDictionary<string, string>? headers = null)
    {
        var allHeaders = new Dictionary<string, string>(headers ?? new Dictionary<string, string>());

        if (contentType is not null)
        {
            allHeaders["Content-Type"] = contentType;
        }

        return CreateRequest(TokenUrl, allHeaders, "POST", Encoding.UTF8.GetBytes(body));
    }

    private static TestHttpRequestData UserRequest(
        string url = "https://api.example/api/v1/org1/proj1/access/issuers",
        IDictionary<string, string>? headers = null,
        string method = "GET",
        string? body = null)
    {
        var request = CreateRequest(url, headers, method, body is null ? null : Encoding.UTF8.GetBytes(body));
        request.FunctionContext.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim>
        {
            new("sub", "user-1"),
            new("scp", Scopes.User.Impersonation),
        };
        return request;
    }

    // A PUT of a user with the issuer as the API's JSON; null sends an empty body.
    private static TestHttpRequestData IssuerRequest(FederatedIssuer? issuer)
    {
        return RawRequest(issuer is null ? string.Empty : FunctionTestDoubles.ToJsonBody(issuer));
    }

    // A PUT of a user with the rules as the API's JSON; null sends an empty body.
    private static TestHttpRequestData RulesRequest(IEnumerable<FederationRule>? rules)
    {
        return RawRequest(rules is null ? string.Empty : FunctionTestDoubles.ToJsonBody(rules.ToList()));
    }

    private static TestHttpRequestData RawRequest(string body)
    {
        return UserRequest(headers: FunctionTestDoubles.JsonContentType(), method: "PUT", body: body);
    }

    // An issuer read from a request body is a copy, so its pins are other instances: compared by content.
    private static void ShouldBeIssuer(object? actual, FederatedIssuer expected)
    {
        var issuer = actual.ShouldBeOfType<FederatedIssuer>();
        JsonSerializer.Serialize(issuer).ShouldBe(JsonSerializer.Serialize(expected));
    }

    private static TestHttpRequestData MachineRequest()
    {
        var request = CreateRequest("https://api.example/api/v1/org1/proj1/access/issuers", null, "GET", null);
        request.FunctionContext.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim>
        {
            new("sub", MachineId),
            new("roles", Scopes.Default.Read),
        };
        request.FunctionContext.Items[FunctionContextItemKeys.MachineIdentity] = MachineId;
        request.FunctionContext.Items[FunctionContextItemKeys.MachineCredentialKind] = MachineCredentialKind.Federated;
        return request;
    }

    private static TestHttpRequestData CreateRequest(string url, IDictionary<string, string>? headers, string method, byte[]? body)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestFunctionContext(services);
        var request = new TestHttpRequestData(context, headers, method, bodyBytes: body, url: new Uri(url));
        ((TestInvocationFeatures)context.Features).Set<IHttpRequestDataFeature>(new TestHttpRequestFeature(request));
        return request;
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(HttpResponseData response)
    {
        var body = response.Body;
        body.Position = 0;
        using var document = await JsonDocument.ParseAsync(body);
        return (response.StatusCode, document.RootElement.Clone());
    }

    private static async Task ShouldBeOAuthErrorAsync(HttpResponseData response, string error, string description)
    {
        var (status, body) = await ReadAsync(response);
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.EnumerateObject().Select(property => property.Name).ShouldBe(["error", "error_description"], ignoreOrder: true);
        body.GetProperty("error").GetString().ShouldBe(error);
        body.GetProperty("error_description").GetString().ShouldBe(description);
        Header(response, "Content-Type").ShouldBe("application/json; charset=utf-8");
        Header(response, "Cache-Control").ShouldBe("no-store");
    }

    private static string Header(HttpResponseData response, string name)
    {
        return string.Join(", ", response.Headers.GetValues(name));
    }

    // Debug builds skip the role checks (DEV_AUTH), so there everything is allowed.
    private static async Task ShouldBeAllowedAsync(Func<Task<IActionResult>> call, bool allowed)
    {
        if (allowed || DevAuth)
        {
            var result = (await call()).ShouldBeAssignableTo<IStatusCodeActionResult>();
            result.ShouldNotBeNull();
            result.StatusCode.ShouldBe(StatusCodes.Status200OK);
        }
        else
        {
            await Should.ThrowAsync<AccessDeniedException>(call);
        }
    }

    // One instance of the pins, so copies of the issuer compare equal.
    private static FederatedIssuer GitHub(string name = "github")
    {
        return SharedGitHub with { Name = name };
    }

    private static FederatedIssuer Custom(string name)
    {
        return new FederatedIssuer { Name = name, Issuer = $"https://{name}.example" };
    }

    private static FederationRule Rule(string subject = "repo:acme/app:ref:refs/heads/main")
    {
        return new FederationRule { Issuer = "github", Audience = Audience, Subject = subject };
    }

    private static MachineAccess FederatedMachine(params FederationRule[] rules)
    {
        return new MachineAccess
        {
            Id = MachineId,
            ObjectId = MachineId,
            AccessKey = string.Empty,
            Scope = MachineAccessScope.DefaultRead,
            CredentialKind = MachineCredentialKind.Federated,
            FederationRules = rules,
        };
    }
}
