using System.Net;
using System.Security.Cryptography;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api;
using _42.Platform.Storyteller.Api.Security;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The applications of one WorkOS environment share the issuer and the keys (WorkOS Multiple Applications), so only the
// audience tells a 2S token from another 42for.net product's. Through the bearer middleware with the real AuthKit
// validators: another application's user token gets 401, also when it carries the environment's shared JWT template
// audience (the single Auth:AuthKit:Audience the list overrides), and the machine validator of the shared issuer does
// not take it either.
// BearerValidationMode.DecodeWithoutValidation is static: the classes that switch it never run in parallel.
[Collection(nameof(BearerValidationMode))]
public sealed class AuthKitAudienceAuthenticationTests : IDisposable
{
    private const string Issuer = "https://auth.example.com";
    private const string ClientId = "client_123";
    private const string SharedTemplateAudience = "https://2s.example.com";
    private const string MachineOrganization = "org_machines";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;
    private readonly IOptions<UserAuthenticationOptions> _options;
    private readonly RecordingMiddleware _middleware = new();

    public AuthKitAudienceAuthenticationTests()
    {
        _key = new RsaSecurityKey(_rsa) { KeyId = "environment-key" };
        _options = Options.Create(new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.AuthKit,
            AuthKit = new AuthKitOptions
            {
                ClientId = ClientId,
                Issuer = Issuer,
                AuthKitDomain = Issuer,
                MachineOrganizationId = MachineOrganization,
                DefaultUserScopes = [Scopes.User.Impersonation],

                // The 2S application is the environment's default one, so its aud is assumed to be its client id here.
                Audience = SharedTemplateAudience,
                Audiences = [ClientId, "client_2s_cli"],
            },
        });
    }

    [Theory]
    [InlineData("client_other")]
    [InlineData(SharedTemplateAudience)]
    public async Task UserTokenOfAnotherApplication_IsUnauthorized_AndNotAMachine(string audience)
    {
        await WithValidationAsync(async () =>
        {
            var context = CreateContext(Sign(audience, clientId: "client_other"));

            (await InvokeAsync(context)).ShouldBeFalse();

            _middleware.Response.ShouldNotBeNull().StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            context.Items.ContainsKey(FunctionContextItemKeys.CachedClaims).ShouldBeFalse();
            context.Items.ContainsKey(FunctionContextItemKeys.MachineIdentity).ShouldBeFalse();
        });
    }

    [Theory]
    [InlineData(ClientId)]
    [InlineData("client_2s_cli")]
    public async Task UserTokenOfA2SApplication_IsAUser(string audience)
    {
        await WithValidationAsync(async () =>
        {
            var context = CreateContext(Sign(audience, clientId: audience));

            (await InvokeAsync(context)).ShouldBeTrue();

            _middleware.Response.ShouldBeNull();
            context.Items.ContainsKey(FunctionContextItemKeys.CachedClaims).ShouldBeTrue();
            context.Items.ContainsKey(FunctionContextItemKeys.MachineIdentity).ShouldBeFalse();
            var request = (await context.GetHttpRequestDataAsync()).ShouldNotBeNull();
            request.GetIdentityUniqueId().ShouldBe("user_01");
        });
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }

    // The validating path, as in Release builds: Debug test runs default to DEV_AUTH's decoding.
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

    private TestFunctionContext CreateContext(string token)
    {
        var user = new AuthKitBearerTokenValidator(
            _options,
            new AuthKitClaimNormalizer(_options),
            new TestHostEnvironment(Environments.Production),
            NullLogger<AuthKitBearerTokenValidator>.Instance,
            new StubKeyManager(KeySet()));
        var machine = new AuthKitMachineTokenValidator(_options, new AuthKitClaimNormalizer(_options), new StubKeyManager(KeySet()));
        var services = FunctionTestDoubles.CreateServices(user, _options.Value, machine);
        var (context, _) = FunctionTestDoubles.CreateRequest(
            services,
            new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
            url: new Uri("https://api.example.com/api/v1/access/account"));
        return context;
    }

    // A user token of the shared issuer and keys. Another application's aud is never the client id, so the machine
    // validator of the shared issuer refuses it at its audience check; its refusal of user_ tokens with the client id as
    // aud is AuthKitImpersonationAuthenticationTests'.
    private string Sign(string audience, string clientId)
    {
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = "user_01",
                ["sid"] = "session_01",
                ["client_id"] = clientId,
                ["email"] = "ada@example.com",
            },
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
        });
    }

    private JsonWebKeySet KeySet()
    {
        var keySet = new JsonWebKeySet();
        keySet.Keys.Add(JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(_rsa.ExportParameters(false)) { KeyId = _key.KeyId }));
        return keySet;
    }

    private async Task<bool> InvokeAsync(TestFunctionContext context)
    {
        var called = false;
        await _middleware.Invoke(context, _ =>
        {
            called = true;
            return Task.CompletedTask;
        });
        return called;
    }

    private sealed class StubKeyManager(JsonWebKeySet keySet) : IConfigurationManager<JsonWebKeySet>
    {
        public Task<JsonWebKeySet> GetConfigurationAsync(CancellationToken cancel)
        {
            return Task.FromResult(keySet);
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
