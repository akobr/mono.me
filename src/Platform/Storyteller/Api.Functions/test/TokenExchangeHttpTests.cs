using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Platform.Storyteller.Api.V1;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using static _42.Platform.Storyteller.Api.Functions.UnitTests.OAuthHttpTesting;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class TokenExchangeHttpTests
{
    private const string Audience = "urn:storyteller:2s.platform:api";
    private const string TokenUrl = "https://api.example/api/v1/access/token";
    private const string SubjectToken = "eyJhbGciOiJSUzI1NiJ9.eyJpc3MiOiJ1c2VyIn0.c2ln";
    private const string ActorToken = "eyJhbGciOiJSUzI1NiJ9.eyJpc3MiOiJhY3RvciJ9.c2ln";
    private const string ClientAssertion = "eyJhbGciOiJSUzI1NiJ9.eyJpc3MiOiJtY3AifQ.c2ln";

    private readonly RecordingUserTokenExchange _exchange = new();
    private readonly FederationOptions _options = new() { Issuer = "urn:storyteller:2s.platform", Audience = Audience };

    private TokenExchangeHttp Http => new(_exchange, Options.Create(_options), NullLogger<TokenExchangeHttp>.Instance);

    // The form and its parameters
    [Fact]
    public async Task Token_ValidForm_IsExchangedAndAnsweredWithSnakeCaseRestrictionsAndNoStore()
    {
        var response = await Http.PostUserToken(Post(TokenUrl, Form(TokenParameters())));

        var (status, body) = await ReadAsync(response);
        status.ShouldBe(HttpStatusCode.OK);
        PropertyNames(body).ShouldBe(["access_token", "issued_token_type", "token_type", "expires_in", "scope", "restrictions"], ignoreOrder: true);
        body.GetProperty("access_token").GetString().ShouldBe("storyteller-user-token");
        body.GetProperty("issued_token_type").GetString().ShouldBe(OAuthTokenExchange.TokenTypes.AccessToken);
        body.GetProperty("token_type").GetString().ShouldBe("Bearer");
        body.GetProperty("expires_in").GetInt32().ShouldBe(600);
        body.GetProperty("scope").GetString().ShouldBe("User.Impersonation");
        Strings(body.GetProperty("restrictions")).ShouldBe(["NoSecretReveal"]);
        Header(response, "Content-Type").ShouldBe("application/json; charset=utf-8");
        Header(response, "Cache-Control").ShouldBe("no-store");
        Header(response, "Pragma").ShouldBe("no-cache");
        response.Headers.Contains("Retry-After").ShouldBeFalse();

        var request = _exchange.Requests.ShouldHaveSingleItem();
        request.GrantType.ShouldBe(OAuthTokenExchange.GrantType);
        request.SubjectToken.ShouldBe(SubjectToken);
        request.SubjectTokenType.ShouldBe(OAuthTokenExchange.TokenTypes.AccessToken);
        request.ClientId.ShouldBe("mcp");
        request.ActorToken.ShouldBe(ActorToken);
        request.ActorTokenType.ShouldBe(OAuthTokenExchange.TokenTypes.Jwt);
        request.ClientAssertion.ShouldBeNull();
        request.ClientAssertionType.ShouldBeNull();
        request.ClientSecret.ShouldBeNull();
        request.Audiences.ShouldBeEmpty();
        request.Resources.ShouldBeEmpty();
        request.Scope.ShouldBeNull();
        request.RequestedTokenType.ShouldBeNull();
    }

    // The endpoint hands every parameter over as it is; the exchange checks the values, in one place.
    [Fact]
    public async Task Token_EveryParameter_IsHandedOverAsItIs()
    {
        var body = Form(
        [
            ("grant_type", "client_credentials"),
            ("subject_token", SubjectToken),
            ("subject_token_type", OAuthTokenExchange.TokenTypes.Jwt),
            ("client_assertion", ClientAssertion),
            ("client_assertion_type", "urn:ietf:params:oauth:client-assertion-type:jwt-bearer"),
            ("client_secret", "dev-secret"),
            ("client_id", "mcp"),
            ("audience", Audience),
            ("audience", "https://other.example"),
            ("resource", Audience),
            ("scope", "User.Impersonation  Annotation.Read"),
            ("requested_token_type", OAuthTokenExchange.TokenTypes.IdToken),
        ]);

        (await ReadAsync(await Http.PostUserToken(Post(TokenUrl, body)))).Status.ShouldBe(HttpStatusCode.OK);

        var request = _exchange.Requests.ShouldHaveSingleItem();
        request.GrantType.ShouldBe("client_credentials");
        request.SubjectTokenType.ShouldBe(OAuthTokenExchange.TokenTypes.Jwt);
        request.ClientAssertion.ShouldBe(ClientAssertion);
        request.ClientAssertionType.ShouldBe("urn:ietf:params:oauth:client-assertion-type:jwt-bearer");
        request.ClientSecret.ShouldBe("dev-secret");
        request.ActorToken.ShouldBeNull();
        request.Audiences.ShouldBe([Audience, "https://other.example"]);
        request.Resources.ShouldBe([Audience]);
        request.Scope.ShouldBe("User.Impersonation  Annotation.Read");
        request.RequestedTokenType.ShouldBe(OAuthTokenExchange.TokenTypes.IdToken);
    }

    [Theory]
    [InlineData("application/x-www-form-urlencoded; charset=utf-8")]
    [InlineData("Application/X-WWW-Form-Urlencoded")]
    public async Task Token_FormContentTypeWithParameters_IsAccepted(string contentType)
    {
        var response = await Http.PostUserToken(Post(TokenUrl, Form(TokenParameters()), contentType));

        (await ReadAsync(response)).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("application/json")]
    [InlineData("multipart/form-data; boundary=x")]
    public async Task Token_OtherContentType_IsInvalidRequest(string? contentType)
    {
        var response = await Http.PostUserToken(Post(TokenUrl, Form(TokenParameters()), contentType));

        await ShouldBeOAuthErrorAsync(response, "invalid_request", "The content type must be application/x-www-form-urlencoded.");
        _exchange.Requests.ShouldBeEmpty();
    }

    // The proofs are in the form; a bearer header would have been validated as an API credential already.
    [Fact]
    public async Task Token_RequestWithAuthorization_IsInvalidRequest()
    {
        var request = Post(TokenUrl, Form(TokenParameters()), headers: new Dictionary<string, string> { ["Authorization"] = "Basic bWNwOnNlY3JldA==" });

        var response = await Http.PostUserToken(request);

        await ShouldBeOAuthErrorAsync(response, "invalid_request", "The token request must not carry an Authorization header.");
        _exchange.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Token_BodyOverTheLimit_IsInvalidRequestWithoutParsing()
    {
        var body = Form(TokenParameters(("padding", new string('a', TokenExchangeHttp.MaxTokenRequestBytes))));

        var response = await Http.PostUserToken(Post(TokenUrl, body));

        await ShouldBeOAuthErrorAsync(response, "invalid_request", "The request body exceeds 65536 bytes.");
        _exchange.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Token_BodyAtTheLimit_IsParsed()
    {
        var form = Form(TokenParameters()) + "&padding=";
        var body = form + new string('a', TokenExchangeHttp.MaxTokenRequestBytes - form.Length);

        var response = await Http.PostUserToken(Post(TokenUrl, body));

        (await ReadAsync(response)).Status.ShouldBe(HttpStatusCode.OK);
    }

    // Decision 7: a subject token and an actor token at the default MaxSubjectTokenBytes do not fit the 32 KiB of the
    // machine endpoint, and do fit here.
    [Fact]
    public void Limit_HoldsTwoTokensAtTheDefaultSize_UnlikeTheMachineEndpoint()
    {
        var token = new string('a', new FederationOptions().MaxSubjectTokenBytes);
        var body = Form(TokenParameters(("subject_token", token), ("actor_token", token)));

        Encoding.UTF8.GetByteCount(body).ShouldBeGreaterThan(FederationHttp.MaxTokenRequestBytes);
        Encoding.UTF8.GetByteCount(body).ShouldBeLessThanOrEqualTo(TokenExchangeHttp.MaxTokenRequestBytes);
    }

    [Theory]
    [InlineData("grant_type")]
    [InlineData("subject_token")]
    [InlineData("actor_token")]
    [InlineData("client_assertion")]
    [InlineData("client_id")]
    [InlineData("client_secret")]
    [InlineData("scope")]
    public async Task Token_ParameterGivenTwice_IsInvalidRequest(string name)
    {
        var body = Form(TokenParameters((name, "first"))) + $"&{name}=again";

        var response = await Http.PostUserToken(Post(TokenUrl, body));

        await ShouldBeOAuthErrorAsync(response, "invalid_request", $"The parameter {name} is given more than once.");
        _exchange.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Token_UnknownParameterGivenTwice_IsInvalidRequestWithoutEchoingIt()
    {
        var body = Form(TokenParameters(("custom", "1"), ("custom", "2")));

        var response = await Http.PostUserToken(Post(TokenUrl, body));

        await ShouldBeOAuthErrorAsync(response, "invalid_request", "A parameter is given more than once.");
    }

    [Fact]
    public async Task Token_EmptyValues_CountAsAbsent()
    {
        // An empty duplicate is not a second value, and an empty parameter is not handed over.
        var body = Form(TokenParameters()) + "&client_id=&scope=&requested_token_type=&audience=&client_secret=";

        var response = await Http.PostUserToken(Post(TokenUrl, body));

        (await ReadAsync(response)).Status.ShouldBe(HttpStatusCode.OK);
        var request = _exchange.Requests.ShouldHaveSingleItem();
        request.ClientId.ShouldBe("mcp");
        request.Scope.ShouldBeNull();
        request.RequestedTokenType.ShouldBeNull();
        request.ClientSecret.ShouldBeNull();
        request.Audiences.ShouldBeEmpty();
    }

    // Missing parameters are the exchange's to name, so the rules hold for every caller.
    [Fact]
    public async Task Token_MissingParameters_AreLeftToTheExchange()
    {
        _exchange.Result = UserTokenExchangeResult.Failure(OAuthTokenExchange.Errors.InvalidRequest, "The parameter subject_token is missing.");

        var response = await Http.PostUserToken(Post(TokenUrl, Form([("grant_type", OAuthTokenExchange.GrantType)])));

        await ShouldBeOAuthErrorAsync(response, "invalid_request", "The parameter subject_token is missing.");
        var request = _exchange.Requests.ShouldHaveSingleItem();
        request.SubjectToken.ShouldBeNull();
        request.ClientId.ShouldBeNull();
    }

    // The results of the exchange
    [Theory]
    [InlineData(OAuthTokenExchange.Errors.InvalidClient, UserTokenExchangeResult.ActorNotAcceptedDescription)]
    [InlineData(OAuthTokenExchange.Errors.InvalidGrant, UserTokenExchangeResult.SubjectNotAcceptedDescription)]
    [InlineData(OAuthTokenExchange.Errors.InvalidGrant, UserTokenExchangeResult.NoAccountDescription)]
    [InlineData(OAuthTokenExchange.Errors.InvalidScope, UserTokenExchangeResult.ScopeNotGrantedDescription)]
    [InlineData(OAuthTokenExchange.Errors.UnsupportedGrantType, UserTokenExchangeResult.DisabledDescription)]
    [InlineData(OAuthTokenExchange.Errors.InvalidTarget, "The audience or resource is not this API.")]
    [InlineData(OAuthTokenExchange.Errors.InvalidRequest, "The subject token type is not supported.")]
    public async Task Token_RefusedExchange_IsTheOAuthErrorWithItsFixedDescription(string error, string description)
    {
        _exchange.Result = UserTokenExchangeResult.Failure(error, description);

        var response = await Http.PostUserToken(Post(TokenUrl, Form(TokenParameters())));

        await ShouldBeOAuthErrorAsync(response, error, description);
        Header(response, "Pragma").ShouldBe("no-cache");
        response.Headers.Contains("Retry-After").ShouldBeFalse();
    }

    [Theory]
    [InlineData(UserTokenExchangeResult.KeysUnavailableDescription)]
    [InlineData(UserTokenExchangeResult.SigningKeyUnavailableDescription)]
    public async Task Token_Unavailable_IsServiceUnavailableWithRetryAfter(string description)
    {
        _exchange.Result = UserTokenExchangeResult.Unavailable(description);

        var response = await Http.PostUserToken(Post(TokenUrl, Form(TokenParameters())));

        var (status, body) = await ReadAsync(response);
        status.ShouldBe(HttpStatusCode.ServiceUnavailable);
        PropertyNames(body).ShouldBe(["error", "error_description"], ignoreOrder: true);
        body.GetProperty("error").GetString().ShouldBe("temporarily_unavailable");
        body.GetProperty("error_description").GetString().ShouldBe(description);
        Header(response, "Retry-After").ShouldBe("5");
        Header(response, "Content-Type").ShouldBe("application/json; charset=utf-8");
        Header(response, "Cache-Control").ShouldBe("no-store");
        Header(response, "Pragma").ShouldBe("no-cache");
    }

    // An actor with AllowSecrets and no other restriction gets an empty list, not a missing member.
    [Fact]
    public async Task Token_WithoutRestrictions_AnswersAnEmptyList()
    {
        _exchange.Result = UserTokenExchangeResult.Success("storyteller-user-token", 300, "User.Impersonation", []);

        var response = await Http.PostUserToken(Post(TokenUrl, Form(TokenParameters())));

        var (_, body) = await ReadAsync(response);
        body.GetProperty("restrictions").ValueKind.ShouldBe(JsonValueKind.Array);
        body.GetProperty("restrictions").GetArrayLength().ShouldBe(0);
        body.GetProperty("expires_in").GetInt32().ShouldBe(300);
    }

    // The token endpoint, an accepted aud of client assertions
    [Theory]
    [InlineData("https://storyteller.example/api")]
    [InlineData("https://storyteller.example/api/")]
    public async Task Token_WithPublicBaseUrl_HandsOverTheConfiguredTokenEndpoint(string publicBaseUrl)
    {
        _options.PublicBaseUrl = publicBaseUrl;

        await Http.PostUserToken(Post(TokenUrl, Form(TokenParameters())));

        _exchange.Requests.ShouldHaveSingleItem().TokenEndpoint.ShouldBe("https://storyteller.example/api/v1/access/token");
    }

    // RFC 7523 clients put the published token_endpoint into aud; it is the one the exchange accepts.
    [Fact]
    public async Task Token_WithPublicBaseUrl_HandsOverTheTokenEndpointTheMetadataPublishes()
    {
        _options.PublicBaseUrl = "https://storyteller.example/api";
        var metadataHttp = new AuthorizationServerHttp(
            new TokenSigningKeyProvider(new InMemoryTokenSigningKeyStore(), Options.Create(_options), TimeProvider.System, NullLogger<TokenSigningKeyProvider>.Instance),
            Options.Create(_options),
            new TestHostEnvironment(Environments.Production),
            NullLogger<AuthorizationServerHttp>.Instance);

        var (_, metadata) = await ReadAsync(await metadataHttp.GetAuthorizationServerMetadata(Get("https://other.example/api/.well-known/oauth-authorization-server")));
        await Http.PostUserToken(Post("https://other.example/api/v1/access/token", Form(TokenParameters())));

        _exchange.Requests.ShouldHaveSingleItem().TokenEndpoint.ShouldNotBeNull().ShouldBe(metadata.GetProperty("token_endpoint").GetString());
    }

    // An assertion the actor made for another server must not pass here because a request named that server's host.
    [Fact]
    public async Task Token_WithoutPublicBaseUrl_HandsOverNoTokenEndpoint_NeverOneOfTheRequest()
    {
        var request = Post(
            "https://other.example/api/v1/access/token",
            Form(TokenParameters()),
            headers: new Dictionary<string, string> { ["Host"] = "other.example", ["X-Forwarded-Host"] = "evil.example" });

        await Http.PostUserToken(request);

        _exchange.Requests.ShouldHaveSingleItem().TokenEndpoint.ShouldBeNull();
    }

    [Fact]
    public void TokenEndpoint_WithoutPublicBaseUrl_IsTheRequestSchemeAndHost_NeverAForwardedHost()
    {
        var request = Get("https://api.example:8443/api/.well-known/oauth-authorization-server", new Dictionary<string, string> { ["X-Forwarded-Host"] = "evil.example" });

        TokenExchangeHttp.GetTokenEndpoint(request, new FederationOptions()).ShouldBe("https://api.example:8443/api/v1/access/token");
    }

    [Theory]
    [InlineData("https://storyteller.example/api")]
    [InlineData("https://storyteller.example/api/")]
    public void TokenEndpoint_WithPublicBaseUrl_UsesIt(string publicBaseUrl)
    {
        TokenExchangeHttp.GetTokenEndpoint(Get(TokenUrl), new FederationOptions { PublicBaseUrl = publicBaseUrl })
            .ShouldBe("https://storyteller.example/api/v1/access/token");
    }

    // The registration of the host: Access.Federation's exchange behind the endpoint, its token verified with the JWKS.
    [Fact]
    public async Task Registration_ExchangesAUserTokenThatTheJwksVerifies()
    {
        const string accountId = "6f1c2d3e-4a5b-4c6d-8e7f-901a2b3c4d5e:3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2d";
        var subjectTokens = new AcceptingSubjectTokenValidator(accountId);
        var access = new FederationAccessService();
        access.Accounts[accountId] = new Account { Id = accountId, UserName = "ada@example.com", Name = "Ada", AccessMap = new Dictionary<string, AccountRole>() };
        using var provider = BuildHost(Environments.Development, subjectTokens, access, new Dictionary<string, string?>
        {
            ["Auth:TokenExchange:Enabled"] = "true",
            ["Auth:TokenExchange:Actors:0:Id"] = "mcp",
            ["Auth:TokenExchange:Actors:0:Authentication:ClientSecretHash"] = Convert.ToHexString(SHA256.HashData("dev-secret"u8)),
            ["Auth:TokenExchange:Actors:0:SubjectAudiences:0"] = "https://mcp.example/mcp",
        });
        var http = ActivatorUtilities.CreateInstance<TokenExchangeHttp>(provider);
        var form = Form(TokenParameters(("actor_token", string.Empty), ("actor_token_type", string.Empty), ("client_secret", "dev-secret")));

        var response = await http.PostUserToken(Post("https://storyteller.example/api/v1/access/token", form));

        var (status, body) = await ReadAsync(response);
        status.ShouldBe(HttpStatusCode.OK, body.ToString());
        body.GetProperty("scope").GetString().ShouldBe("User.Impersonation");
        Strings(body.GetProperty("restrictions")).ShouldBe(["NoSecretReveal"]);
        body.GetProperty("expires_in").GetInt32().ShouldBeInRange(590, 600);
        subjectTokens.Audiences.ShouldBe(["https://mcp.example/mcp"]);

        var keys = await ActivatorUtilities.CreateInstance<AuthorizationServerHttp>(provider)
            .GetJsonWebKeySet(Get("https://storyteller.example/api/.well-known/jwks.json"));
        var jwks = new JsonWebKeySet((await ReadAsync(keys)).Body.GetRawText());
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(body.GetProperty("access_token").GetString(), new TokenValidationParameters
        {
            IssuerSigningKeys = jwks.GetSigningKeys(),
            ValidIssuer = "https://storyteller.example",
            ValidAudience = "https://storyteller.example/api",
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        });

        validation.IsValid.ShouldBeTrue(validation.Exception?.Message);
        var token = (JsonWebToken)validation.SecurityToken;
        token.Typ.ShouldBe("at+jwt");
        token.Subject.ShouldBe(accountId);
        token.GetClaim("token_use").Value.ShouldBe("user");
        token.GetClaim("client_id").Value.ShouldBe("mcp");
        using var payload = JsonDocument.Parse(Base64UrlEncoder.Decode(token.EncodedPayload));
        payload.RootElement.GetProperty("act").GetProperty("sub").GetString().ShouldBe("mcp");
        Strings(payload.RootElement.GetProperty("restrictions")).ShouldBe(["NoSecretReveal"]);
    }

    [Fact]
    public async Task Registration_WhileTheExchangeIsOff_AnswersUnsupportedGrantType()
    {
        using var provider = BuildHost(Environments.Production, new AcceptingSubjectTokenValidator("account"), new FederationAccessService(), []);
        var http = ActivatorUtilities.CreateInstance<TokenExchangeHttp>(provider);

        var response = await http.PostUserToken(Post("https://storyteller.example/api/v1/access/token", Form(TokenParameters())));

        await ShouldBeOAuthErrorAsync(response, "unsupported_grant_type", UserTokenExchangeResult.DisabledDescription);
    }

    private static (string Name, string Value)[] TokenParameters(params (string Name, string Value)[] replaced)
    {
        (string Name, string Value)[] parameters =
        [
            ("grant_type", OAuthTokenExchange.GrantType),
            ("subject_token", SubjectToken),
            ("subject_token_type", OAuthTokenExchange.TokenTypes.AccessToken),
            ("client_id", "mcp"),
            ("actor_token", ActorToken),
            ("actor_token_type", OAuthTokenExchange.TokenTypes.Jwt),
        ];

        // A named parameter replaces the default one; an empty value drops it.
        var names = replaced.Select(parameter => parameter.Name).ToHashSet(StringComparer.Ordinal);
        return
        [
            .. parameters.Where(parameter => !names.Contains(parameter.Name)),
            .. replaced.Where(parameter => parameter.Value.Length > 0),
        ];
    }

    // Program.cs's registration of the federation and the user exchange, with a provider double for the subject tokens.
    private static ServiceProvider BuildHost(
        string environment,
        IUserSubjectTokenValidator subjectTokens,
        IAccessService access,
        Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Provider"] = "EntraId",
                ["Auth:TenantId"] = "6f1c2d3e-4a5b-4c6d-8e7f-901a2b3c4d5e",
                ["Auth:ClientId"] = "9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d",
                ["MachineAuth:Federation:Issuer"] = "https://storyteller.example",
                ["MachineAuth:Federation:Audience"] = "https://storyteller.example/api",
                ["MachineAuth:Federation:PublicBaseUrl"] = "https://storyteller.example/api",
            })
            .AddInMemoryCollection(settings)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environment));
        services.AddSingleton(access);
        services.AddSingleton(subjectTokens);
        services.AddFederatedMachineAccess(configuration);
        services.AddUserTokenExchange(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static async Task ShouldBeOAuthErrorAsync(Microsoft.Azure.Functions.Worker.Http.HttpResponseData response, string error, string description)
    {
        var (status, body) = await ReadAsync(response);
        status.ShouldBe(HttpStatusCode.BadRequest);
        PropertyNames(body).ShouldBe(["error", "error_description"], ignoreOrder: true);
        body.GetProperty("error").GetString().ShouldBe(error);
        body.GetProperty("error_description").GetString().ShouldBe(description);
        Header(response, "Content-Type").ShouldBe("application/json; charset=utf-8");
        Header(response, "Cache-Control").ShouldBe("no-store");
    }

    // Accepts every subject token as a token of the account, valid for an hour.
    private sealed class AcceptingSubjectTokenValidator(string accountId) : IUserSubjectTokenValidator
    {
        public List<string> Audiences { get; } = [];

        public Task<UserSubjectTokenResult> ValidateAsync(
            string subjectToken,
            IReadOnlyCollection<string> audiences,
            IReadOnlyCollection<string> allowedClientIds,
            CancellationToken cancellationToken = default)
        {
            Audiences.AddRange(audiences);
            return Task.FromResult(UserSubjectTokenResult.Accepted(
                accountId,
                "mcp-client",
                DateTimeOffset.UtcNow.AddHours(1),
                authTime: null,
                tokenId: Guid.NewGuid().ToString("N"),
                claims: [new Claim("sub", accountId)]));
        }
    }
}
