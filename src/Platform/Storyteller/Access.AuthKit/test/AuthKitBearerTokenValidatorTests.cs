using System.Net;
using System.Security.Cryptography;
using System.Text;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Access.AuthKit.UnitTests;

public class AuthKitBearerTokenValidatorTests
{
    private const string ClientId = "client_123";
    private const string Issuer = "https://api.workos.com/";
    private const string JwksUri = "https://api.workos.com/sso/jwks/client_123";

    [Fact]
    public async Task Validate_UserToken_ReturnsNormalizedUserClaims()
    {
        using var fixture = new TokenFixture(configure: authKit =>
        {
            authKit.DefaultUserScopes = ["User.Impersonation", "Default.Read"];
            authKit.PermissionMap = new Dictionary<string, string> { ["storyteller:configuration-secrets"] = "Configuration.Secrets" };
        });
        var rawToken = fixture.CreateToken(new Dictionary<string, object>
        {
            ["sub"] = "user_01",
            ["sid"] = "session_01",
            ["org_id"] = "org_01",
            ["email"] = "ada@example.com",
            ["name"] = "Ada Lovelace",
            ["permissions"] = new[] { "storyteller:configuration-secrets", "unmapped" },
        });

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldNotBeNull();
        result.IsMachine.ShouldBeFalse();
        result.MachineId.ShouldBeNull();
        Value(result, "sub").ShouldBe("user_01");
        Value(result, "org_id").ShouldBe("org_01");
        Value(result, "name").ShouldBe("Ada Lovelace");
        Value(result, "preferred_username").ShouldBe("ada@example.com");
        Value(result, "scp").ShouldBe("User.Impersonation Default.Read Configuration.Secrets");
    }

    [Theory]
    [InlineData("https://storyteller.42for.net", true)]
    [InlineData("https://someone-else.example", false)]
    [InlineData(null, false)]
    public async Task Validate_ConfiguredAudience_IsRequired(string? tokenAudience, bool accepted)
    {
        using var fixture = new TokenFixture(configure: authKit => authKit.Audience = "https://storyteller.42for.net");
        var rawToken = fixture.CreateToken(UserClaims(), audience: tokenAudience);

        var result = await fixture.Validator.ValidateAsync(rawToken);

        (result is not null).ShouldBe(accepted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://anything.example")]
    public async Task Validate_NoConfiguredAudience_DoesNotCheckTheAudience(string? tokenAudience)
    {
        using var fixture = new TokenFixture();
        var rawToken = fixture.CreateToken(UserClaims(), audience: tokenAudience);

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("https://api.workos.com.evil/")]
    [InlineData("https://API.workos.com/")]
    [InlineData("https://api.workos.com/user_management/client_123")]
    [InlineData("https://api.workos.com//")]
    public async Task Validate_WrongIssuer_ReturnsNull(string issuer)
    {
        using var fixture = new TokenFixture();
        var rawToken = fixture.CreateToken(UserClaims(), issuer: issuer);

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldBeNull();
    }

    [Theory]
    [InlineData("https://api.workos.com/", "https://api.workos.com")]
    [InlineData("https://auth.example.com", "https://auth.example.com/")]
    [InlineData("https://auth.example.com", "https://auth.example.com")]
    public async Task Validate_IssuerMayDifferOnlyByTheTrailingSlash(string configuredIssuer, string tokenIssuer)
    {
        using var fixture = new TokenFixture(configure: authKit => authKit.Issuer = configuredIssuer);
        var rawToken = fixture.CreateToken(UserClaims(), issuer: tokenIssuer);

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldNotBeNull();
    }

    [Fact]
    public async Task Validate_ExpiredWithinTheClockSkew_IsAccepted()
    {
        using var fixture = new TokenFixture();
        var rawToken = fixture.CreateToken(UserClaims(), expires: DateTime.UtcNow.AddSeconds(-10));

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldNotBeNull();
    }

    [Fact]
    public async Task Validate_ExpiredBeyondTheClockSkew_ReturnsNull()
    {
        // Two minutes is inside the Entra default of five, so this pins the 30-second AuthKit skew.
        using var fixture = new TokenFixture();
        var rawToken = fixture.CreateToken(UserClaims(), expires: DateTime.UtcNow.AddMinutes(-2));

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Validate_TokenNotYetValid_ReturnsNull()
    {
        using var fixture = new TokenFixture();
        var rawToken = fixture.CreateToken(
            UserClaims(),
            notBefore: DateTime.UtcNow.AddMinutes(2),
            expires: DateTime.UtcNow.AddMinutes(10));

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Validate_HmacToken_ReturnsNull()
    {
        using var fixture = new TokenFixture();
        var symmetric = new SymmetricSecurityKey("0123456789abcdef0123456789abcdef"u8.ToArray()) { KeyId = "test-key" };
        var rawToken = fixture.CreateToken(UserClaims(), signingKey: symmetric, algorithm: SecurityAlgorithms.HmacSha256);

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Validate_Rs512WithTheRightKey_ReturnsNull()
    {
        using var fixture = new TokenFixture();
        var rawToken = fixture.CreateToken(UserClaims(), algorithm: SecurityAlgorithms.RsaSha512);

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Validate_UnsignedToken_ReturnsNull()
    {
        using var fixture = new TokenFixture();
        var header = Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""");
        var exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
        var payload = Base64UrlEncoder.Encode($$"""{"iss":"{{Issuer}}","sub":"user_01","exp":{{exp}}}""");

        var result = await fixture.Validator.ValidateAsync($"{header}.{payload}.");

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Validate_UnknownSigningKey_ReturnsNullAndRequestsAKeyRefresh()
    {
        using var fixture = new TokenFixture();
        using var otherRsa = RSA.Create(2048);
        var otherKey = new RsaSecurityKey(otherRsa) { KeyId = "rotated-key" };
        var rawToken = fixture.CreateToken(UserClaims(), signingKey: otherKey);

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldBeNull();
        fixture.Manager.RefreshRequests.ShouldBe(1);
    }

    [Theory]
    [InlineData("client_01")]
    [InlineData("01HUSER")]
    [InlineData("User_01")]
    public async Task Validate_SubjectThatIsNotAWorkOsUser_ReturnsNull(string subject)
    {
        using var fixture = new TokenFixture();
        var rawToken = fixture.CreateToken(new Dictionary<string, object> { ["sub"] = subject });

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Validate_MissingSubject_ReturnsNull()
    {
        using var fixture = new TokenFixture();
        var rawToken = fixture.CreateToken(new Dictionary<string, object> { ["email"] = "ada@example.com" });

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-jwt")]
    public async Task Validate_UnreadableToken_ReturnsNullWithoutFetchingKeys(string rawToken)
    {
        using var fixture = new TokenFixture();

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldBeNull();
        fixture.Manager.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Validate_KeyRetrievalFailure_Throws()
    {
        using var fixture = new TokenFixture(failure: new HttpRequestException("jwks down"));
        var rawToken = fixture.CreateToken(UserClaims());

        var exception = await Should.ThrowAsync<BearerKeyRetrievalException>(() => fixture.Validator.ValidateAsync(rawToken));

        exception.InnerException.ShouldBeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task Validate_Cancellation_Propagates()
    {
        using var fixture = new TokenFixture();
        var rawToken = fixture.CreateToken(UserClaims());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => fixture.Validator.ValidateAsync(rawToken, cancellation.Token));
    }

    [Fact]
    public async Task Validate_KeysServedByTheJwksEndpoint_AreFetchedOnceAndReused()
    {
        using var fixture = new TokenFixture();
        var handler = new StubJwksHandler(HttpStatusCode.OK, fixture.JwksJson);
        var validator = fixture.CreateValidator(CreateHttpKeyManager(handler));
        var rawToken = fixture.CreateToken(UserClaims());

        (await validator.ValidateAsync(rawToken)).ShouldNotBeNull();
        (await validator.ValidateAsync(rawToken)).ShouldNotBeNull();

        handler.Requests.ShouldBe([JwksUri]);
    }

    [Fact]
    public async Task Validate_JwksEndpointFailure_ThrowsKeyRetrievalException()
    {
        using var fixture = new TokenFixture();
        var handler = new StubJwksHandler(HttpStatusCode.InternalServerError, "{}");
        var validator = fixture.CreateValidator(CreateHttpKeyManager(handler));
        var rawToken = fixture.CreateToken(UserClaims());

        await Should.ThrowAsync<BearerKeyRetrievalException>(() => validator.ValidateAsync(rawToken));
    }

    [Fact]
    public void Constructor_BlankIssuer_Throws()
    {
        var options = Options.Create(new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.AuthKit,
            AuthKit = new AuthKitOptions { ClientId = ClientId, Issuer = " " },
        });

        Should.Throw<InvalidOperationException>(() => new AuthKitBearerTokenValidator(
            options,
            new AuthKitClaimNormalizer(options),
            new TestHostEnvironment()));
    }

    private static ConfigurationManager<JsonWebKeySet> CreateHttpKeyManager(HttpMessageHandler handler)
    {
        return new ConfigurationManager<JsonWebKeySet>(
            JwksUri,
            new JsonWebKeySetRetriever(),
            new HttpDocumentRetriever(new HttpClient(handler)));
    }

    private static Dictionary<string, object> UserClaims()
    {
        return new Dictionary<string, object> { ["sub"] = "user_01" };
    }

    private static string? Value(BearerValidationResult result, string type)
    {
        return result.Claims.FirstOrDefault(claim => claim.Type == type)?.Value;
    }

    private sealed class TokenFixture : IDisposable
    {
        private readonly IOptions<UserAuthenticationOptions> _options;

        public TokenFixture(Action<AuthKitOptions>? configure = null, Exception? failure = null)
        {
            Rsa = RSA.Create(2048);
            Key = new RsaSecurityKey(Rsa) { KeyId = "test-key" };

            var parameters = Rsa.ExportParameters(includePrivateParameters: false);
            JwksJson = $$"""
                {"keys":[{"kty":"RSA","use":"sig","alg":"RS256","kid":"test-key","n":"{{Base64UrlEncoder.Encode(parameters.Modulus)}}","e":"{{Base64UrlEncoder.Encode(parameters.Exponent)}}"}]}
                """;

            var authKit = new AuthKitOptions { ClientId = ClientId };
            configure?.Invoke(authKit);
            _options = Options.Create(new UserAuthenticationOptions
            {
                Provider = IdentityProviderKind.AuthKit,
                AuthKit = authKit,
            });

            Manager = new StaticKeyManager(new JsonWebKeySet(JwksJson), failure);
            Validator = CreateValidator(Manager);
        }

        public RSA Rsa { get; }

        public RsaSecurityKey Key { get; }

        public string JwksJson { get; }

        public StaticKeyManager Manager { get; }

        public AuthKitBearerTokenValidator Validator { get; }

        public AuthKitBearerTokenValidator CreateValidator(IConfigurationManager<JsonWebKeySet> manager)
        {
            return new AuthKitBearerTokenValidator(
                _options,
                new AuthKitClaimNormalizer(_options),
                new TestHostEnvironment(),
                manager);
        }

        public string CreateToken(
            Dictionary<string, object> claims,
            string? issuer = Issuer,
            string? audience = null,
            DateTime? expires = null,
            DateTime? notBefore = null,
            SecurityKey? signingKey = null,
            string? algorithm = null)
        {
            var expiresAt = expires ?? DateTime.UtcNow.AddMinutes(10);
            var notBeforeAt = notBefore ?? expiresAt.AddMinutes(-15);
            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = issuer,
                Audience = audience,
                Claims = claims,
                IssuedAt = notBeforeAt,
                NotBefore = notBeforeAt,
                Expires = expiresAt,
                SigningCredentials = new SigningCredentials(signingKey ?? Key, algorithm ?? SecurityAlgorithms.RsaSha256),
            };

            return new JsonWebTokenHandler().CreateToken(descriptor);
        }

        public void Dispose()
        {
            Rsa.Dispose();
        }
    }

    private sealed class StaticKeyManager : IConfigurationManager<JsonWebKeySet>
    {
        private readonly JsonWebKeySet _keySet;
        private readonly Exception? _failure;

        public StaticKeyManager(JsonWebKeySet keySet, Exception? failure)
        {
            _keySet = keySet;
            _failure = failure;
        }

        public int Calls { get; private set; }

        public int RefreshRequests { get; private set; }

        public Task<JsonWebKeySet> GetConfigurationAsync(CancellationToken cancel)
        {
            Calls++;
            cancel.ThrowIfCancellationRequested();

            if (_failure is not null)
            {
                throw _failure;
            }

            return Task.FromResult(_keySet);
        }

        public void RequestRefresh()
        {
            RefreshRequests++;
        }
    }

    private sealed class StubJwksHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubJwksHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
