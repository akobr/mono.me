using System.Net;
using System.Security.Cryptography;
using System.Text;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

using static _42.Platform.Storyteller.Api.Functions.UnitTests.OAuthHttpTesting;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The user token exchange per provider, composed as Program.cs composes it: the federation, the provider's user
// authentication (its IUserSubjectTokenValidator, registered by its entry point) and the exchange. Only the provider's
// key source is a stub, registered before the entry point, whose TryAdd then keeps it. A person's token of the provider
// becomes a Storyteller user token, which the bearer middleware accepts on an API route as the person through the
// actor. The providers' own rules are tested in their projects, the exchange's mapping with a fake in Access.Federation.
// BearerValidationMode.DecodeWithoutValidation is static: the classes that switch it never run in parallel.
[Collection(nameof(BearerValidationMode))]
public sealed class UserTokenExchangeProviderTests : IDisposable
{
    private const string StorytellerIssuer = "https://storyteller.example";
    private const string StorytellerAudience = "https://storyteller.example/api";
    private const string TokenUrl = "https://storyteller.example/api/v1/access/token";
    private const string ActorId = "mcp";
    private const string ActorSecret = "dev-secret";
    private const string McpResource = "https://mcp.example/mcp";

    private const string TenantId = "6f1c2d3e-4a5b-4c6d-8e7f-901a2b3c4d5e";
    private const string ObjectId = "3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2d";
    private const string ApiClientId = "9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d";
    private const string McpClientAppId = "2b1a0c9d-8e7f-4a6b-9c5d-4e3f2a1b0c9d";

    private const string AuthKitIssuer = "https://auth.example.com";
    private const string AuthKitUserId = "user_01HZY8Q5M3J7K9N2P4R6T8V0X1";

    private const string KeycloakIssuer = "https://keycloak.example/realms/storyteller";
    private const string KeycloakUserId = "5f1c2b3a-9d8e-4f70-8a6b-1c2d3e4f5a6b";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;
    private readonly FederationAccessService _access = new();
    private readonly List<ServiceProvider> _providers = [];

    public UserTokenExchangeProviderTests()
    {
        _key = new RsaSecurityKey(_rsa) { KeyId = "provider-key" };
    }

    public static TheoryData<string> Providers => new() { "EntraId", "AuthKit", "Keycloak" };

    public static TheoryData<string, Dictionary<string, object>, string?> NotAPerson => new()
    {
        // An app-only token: idtyp app and no scp.
        { "EntraId", new Dictionary<string, object> { ["idtyp"] = "app", ["roles"] = "Mcp.Access" }, null },

        // An M2M token of the AuthKit issuer names its application, not a user.
        { "AuthKit", new Dictionary<string, object>(), "client_01HZY8Q5M3J7K9N2P4R6T8V0X1" },

        // A service account of the realm.
        { "Keycloak", new Dictionary<string, object> { ["preferred_username"] = "service-account-mcp-client" }, null },
    };

    // A person's token of each provider is exchanged for the account the provider's rules name, and the issued token is
    // the person through the actor on API routes, with the actor's restriction.
    [Theory]
    [MemberData(nameof(Providers))]
    public async Task PersonToken_IsExchanged_AndTheIssuedTokenIsThePersonThroughTheActor(string provider)
    {
        await WithValidationAsync(async () =>
        {
            var accountId = AccountId(provider);
            AddAccount(accountId);
            var services = BuildHost(provider);

            var response = await ExchangeAsync(services, PersonToken(provider));

            var (status, body) = await ReadAsync(response);
            status.ShouldBe(HttpStatusCode.OK, body.ToString());
            Strings(body.GetProperty("restrictions")).ShouldBe([ActorRestrictions.NoSecretReveal]);
            var accessToken = body.GetProperty("access_token").GetString()!;
            new JsonWebToken(accessToken).Subject.ShouldBe(accountId);

            var middleware = new RecordingMiddleware();
            var (context, request) = FunctionTestDoubles.CreateRequest(services, new Dictionary<string, string> { ["Authorization"] = $"Bearer {accessToken}" });
            var called = false;
            await middleware.Invoke(context, _ =>
            {
                called = true;
                return Task.CompletedTask;
            });

            called.ShouldBeTrue(middleware.Response?.StatusCode.ToString());
            request.GetIdentityUniqueId().ShouldBe(accountId);
            context.Items[FunctionContextItemKeys.DelegatedActor].ShouldBe(ActorId);
            context.Items.ContainsKey(FunctionContextItemKeys.MachineIdentity).ShouldBeFalse();
            request.HasActorRestriction(ActorRestrictions.NoSecretReveal).ShouldBeTrue();
        });
    }

    // The provider's own rules refuse the token: the generic text, the reason only in the log.
    [Theory]
    [MemberData(nameof(NotAPerson))]
    public async Task TokenTheProviderRulesRefuse_IsInvalidGrantWithTheGenericText(string provider, Dictionary<string, object> claims, string? subject)
    {
        await WithValidationAsync(async () =>
        {
            AddAccount(AccountId(provider));
            var services = BuildHost(provider);

            var response = await ExchangeAsync(services, PersonToken(provider, claims, subject));

            var (status, body) = await ReadAsync(response);
            status.ShouldBe(HttpStatusCode.BadRequest, body.ToString());
            body.GetProperty("error").GetString().ShouldBe(OAuthTokenExchange.Errors.InvalidGrant);
            body.GetProperty("error_description").GetString().ShouldBe(UserTokenExchangeResult.SubjectNotAcceptedDescription);
        });
    }

    // The provider's keys cannot be retrieved: the exchange is unavailable, not refused.
    [Theory]
    [MemberData(nameof(Providers))]
    public async Task ProviderKeysUnavailable_IsServiceUnavailableWithRetryAfter(string provider)
    {
        await WithValidationAsync(async () =>
        {
            AddAccount(AccountId(provider));
            var services = BuildHost(provider, new HttpRequestException("The keys are not reachable."));

            var response = await ExchangeAsync(services, PersonToken(provider));

            var (status, body) = await ReadAsync(response);
            status.ShouldBe(HttpStatusCode.ServiceUnavailable, body.ToString());
            body.GetProperty("error").GetString().ShouldBe("temporarily_unavailable");
            body.GetProperty("error_description").GetString().ShouldBe(UserTokenExchangeResult.KeysUnavailableDescription);
            Header(response, "Retry-After").ShouldBe("5");
        });
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        _rsa.Dispose();
    }

    private static string AccountId(string provider)
    {
        return provider switch
        {
            "EntraId" => $"{TenantId}:{ObjectId}",
            "AuthKit" => AuthKitUserId,
            _ => KeycloakUserId,
        };
    }

    private static async Task WithValidationAsync(Func<Task> body)
    {
        var previous = BearerValidationMode.DecodeWithoutValidation;
        BearerValidationMode.DecodeWithoutValidation = false;

        try
        {
            await body();
        }
        finally
        {
            BearerValidationMode.DecodeWithoutValidation = previous;
        }
    }

    private static Dictionary<string, string?> ProviderSettings(string provider)
    {
        return provider switch
        {
            "EntraId" => new Dictionary<string, string?>
            {
                ["Auth:Provider"] = "EntraId",
                ["Auth:TenantId"] = TenantId,
                ["Auth:ClientId"] = ApiClientId,
            },
            "AuthKit" => new Dictionary<string, string?>
            {
                ["Auth:Provider"] = "AuthKit",
                ["Auth:AuthKit:ClientId"] = "client_storyteller",
                ["Auth:AuthKit:Issuer"] = AuthKitIssuer,
            },
            _ => new Dictionary<string, string?>
            {
                ["Auth:Provider"] = "Keycloak",
                ["Auth:Keycloak:Issuer"] = KeycloakIssuer,
                ["Auth:Keycloak:Audience"] = "storyteller-api",
                ["Auth:Keycloak:AllowedClientIds:0"] = "sform",
            },
        };
    }

    private static async Task<HttpResponseData> ExchangeAsync(IServiceProvider services, string subjectToken)
    {
        var http = ActivatorUtilities.CreateInstance<TokenExchangeHttp>(services);
        var form = Form(
        [
            ("grant_type", OAuthTokenExchange.GrantType),
            ("subject_token", subjectToken),
            ("subject_token_type", OAuthTokenExchange.TokenTypes.AccessToken),
            ("client_id", ActorId),
            ("client_secret", ActorSecret),
        ]);

        return await http.PostUserToken(Post(TokenUrl, form));
    }

    private void AddAccount(string accountId)
    {
        _access.Accounts[accountId] = new Account
        {
            Id = accountId,
            UserName = "ada@example.com",
            Name = "Ada Lovelace",
            AccessMap = new Dictionary<string, AccountRole>(),
        };
    }

    // Program.cs's order: the federation, the provider's user authentication, the exchange. The provider's bearer
    // validator is registered first with a stub key source; the entry point's TryAddSingleton keeps it. The validators
    // get a Production environment of their own, so they do not turn on IdentityModel's PII logging for the process.
    private ServiceProvider BuildHost(string provider, Exception? keyFailure = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MachineAuth:Federation:Issuer"] = StorytellerIssuer,
                ["MachineAuth:Federation:Audience"] = StorytellerAudience,
                ["MachineAuth:Federation:PublicBaseUrl"] = StorytellerAudience,
                ["Auth:TokenExchange:Enabled"] = "true",
                ["Auth:TokenExchange:Actors:0:Id"] = ActorId,
                ["Auth:TokenExchange:Actors:0:Authentication:ClientSecretHash"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ActorSecret))),
                ["Auth:TokenExchange:Actors:0:SubjectAudiences:0"] = McpResource,
            })
            .AddInMemoryCollection(ProviderSettings(provider))
            .Build();
        var validatorEnvironment = new TestHostEnvironment(Environments.Production);

        var services = new ServiceCollection();
        services.AddLogging();

        // ClientSecretHash, the actor proof of local development, is accepted in Development only.
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(Environments.Development));
        services.AddSingleton<IAccessService>(_access);
        services.AddFederatedMachineAccess(configuration);

        switch (provider)
        {
            case "EntraId":
                services.AddSingleton(sp => new EntraIdBearerTokenValidator(
                    sp.GetRequiredService<IOptions<UserAuthenticationOptions>>(),
                    validatorEnvironment,
                    new StubConfigurationManager<OpenIdConnectConfiguration>(OpenIdConfiguration($"https://login.microsoftonline.com/{TenantId}/v2.0"), keyFailure)));
                services.AddEntraIdUserAuthentication(configuration);
                break;
            case "AuthKit":
                services.AddSingleton(sp => new AuthKitBearerTokenValidator(
                    sp.GetRequiredService<IOptions<UserAuthenticationOptions>>(),
                    sp.GetRequiredService<AuthKitClaimNormalizer>(),
                    validatorEnvironment,
                    new StubConfigurationManager<JsonWebKeySet>(KeySet(), keyFailure)));
                services.AddAuthKitUserAuthentication(configuration);
                break;
            default:
                services.AddSingleton(sp => new KeycloakBearerTokenValidator(
                    sp.GetRequiredService<IOptions<UserAuthenticationOptions>>(),
                    sp.GetRequiredService<KeycloakClaimNormalizer>(),
                    new StubConfigurationManager<OpenIdConnectConfiguration>(OpenIdConfiguration(KeycloakIssuer), keyFailure)));
                services.AddKeycloakUserAuthentication(configuration);
                break;
        }

        services.AddUserTokenExchange(configuration);

        var built = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        _providers.Add(built);
        return built;
    }

    // A token of a person (or, with claims and subject, of what the provider's rules refuse) for the MCP resource.
    private string PersonToken(string provider, Dictionary<string, object>? claims = null, string? subject = null)
    {
        var payload = new Dictionary<string, object>();
        string issuer;

        switch (provider)
        {
            case "EntraId":
                issuer = $"https://login.microsoftonline.com/{TenantId}/v2.0";
                payload["tid"] = TenantId;
                payload["oid"] = ObjectId;
                payload["sub"] = subject ?? "pairwise-subject";
                payload["azp"] = McpClientAppId;
                payload["ver"] = "2.0";

                // An app-only token carries roles instead.
                if (claims?.ContainsKey("idtyp") != true)
                {
                    payload["scp"] = "Mcp.Access";
                }

                break;
            case "AuthKit":
                issuer = AuthKitIssuer;
                payload["sub"] = subject ?? AuthKitUserId;
                payload["client_id"] = "client_mcp";
                payload["sid"] = "session_01";
                break;
            default:
                issuer = KeycloakIssuer;
                payload["sub"] = subject ?? KeycloakUserId;
                payload["azp"] = "mcp-client";
                payload["typ"] = "Bearer";
                payload["preferred_username"] = "ada";
                payload["name"] = "Ada Lovelace";
                break;
        }

        foreach (var (name, value) in claims ?? [])
        {
            payload[name] = value;
        }

        payload["jti"] = Guid.NewGuid().ToString("N");

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = McpResource,
            Claims = payload,
            IssuedAt = DateTime.UtcNow,
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
        });
    }

    private OpenIdConnectConfiguration OpenIdConfiguration(string issuer)
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = issuer };
        configuration.SigningKeys.Add(_key);
        return configuration;
    }

    private JsonWebKeySet KeySet()
    {
        var keySet = new JsonWebKeySet();
        keySet.Keys.Add(JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(_rsa.ExportParameters(false)) { KeyId = _key.KeyId }));
        return keySet;
    }

    // The provider's metadata or key set without HTTP; a failure is thrown on every read.
    private sealed class StubConfigurationManager<T>(T configuration, Exception? failure) : IConfigurationManager<T>
        where T : class
    {
        public Task<T> GetConfigurationAsync(CancellationToken cancel)
        {
            return failure is null ? Task.FromResult(configuration) : Task.FromException<T>(failure);
        }

        public void RequestRefresh()
        {
        }
    }

    private sealed class RecordingMiddleware : BearerAuthenticationMiddleware
    {
        public RecordingMiddleware()
            : base(NullLogger<BearerAuthenticationMiddleware>.Instance)
        {
        }

        public HttpResponseData? Response { get; private set; }

        protected override void AssignResponse(FunctionContext context, HttpResponseData response)
        {
            Response = response;
        }
    }
}
