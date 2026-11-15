using System.Net;
using System.Security.Cryptography;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api;
using _42.Platform.Storyteller.Api.Security;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// A WorkOS impersonation token (an AuthKit user token with act) through the bearer middleware with the real AuthKit
// validator: 401 on any route, as the middleware does not look at the route, and no machine validator takes it.
// BearerValidationMode.DecodeWithoutValidation is static: the classes that switch it never run in parallel.
[Collection(nameof(BearerValidationMode))]
public sealed class AuthKitImpersonationAuthenticationTests : IDisposable
{
    private const string Issuer = "https://auth.example.com";
    private const string StorytellerIssuer = "urn:storyteller:2s.platform";
    private const string MachineOrganization = "org_machines";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;
    private readonly IOptions<UserAuthenticationOptions> _options;
    private readonly CapturingLogger<AuthKitBearerTokenValidator> _logger = new();
    private readonly RecordingMiddleware _middleware = new();

    public AuthKitImpersonationAuthenticationTests()
    {
        _key = new RsaSecurityKey(_rsa) { KeyId = "user-key" };
        _options = Options.Create(new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.AuthKit,
            AuthKit = new AuthKitOptions
            {
                ClientId = "client_123",
                Issuer = Issuer,
                AuthKitDomain = Issuer,
                MachineOrganizationId = MachineOrganization,
                DefaultUserScopes = [Scopes.User.Impersonation],
            },
        });
    }

    [Theory]
    [InlineData("v1/access/account")]
    [InlineData("v1/auth/configuration")]
    public async Task ImpersonationToken_IsUnauthorized(string route)
    {
        await WithValidationAsync(async () =>
        {
            var storyteller = new RecordingMachineTokenValidator(StorytellerIssuer) { Owns = true };
            var context = CreateContext(Sign(ImpersonationClaims()), route, storyteller);

            (await InvokeAsync(context)).ShouldBeFalse();

            _middleware.Response.ShouldNotBeNull().StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            context.Items.ContainsKey(FunctionContextItemKeys.CachedClaims).ShouldBeFalse();
            storyteller.Calls.ShouldBe(0);
            var entry = _logger.Entries.ShouldHaveSingleItem();
            entry.Level.ShouldBe(LogLevel.Warning);
            entry.Message.ShouldContain("admin@example.com");
        });
    }

    // With a custom AuthKit domain, users and machines share the issuer: the machine validator is asked after the user
    // validator refuses, and refuses a user_ token too.
    [Fact]
    public async Task ImpersonationToken_OfASharedIssuer_IsNotTakenAsAMachine()
    {
        await WithValidationAsync(async () =>
        {
            var machine = new AuthKitMachineTokenValidator(_options, new AuthKitClaimNormalizer(_options), new StubKeyManager(KeySet()));
            var context = CreateContext(Sign(ImpersonationClaims()), "v1/access/account", machine);

            (await InvokeAsync(context)).ShouldBeFalse();

            _middleware.Response.ShouldNotBeNull().StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            context.Items.ContainsKey(FunctionContextItemKeys.CachedClaims).ShouldBeFalse();
            context.Items.ContainsKey(FunctionContextItemKeys.MachineIdentity).ShouldBeFalse();
        });
    }

    [Fact]
    public async Task UserTokenWithoutAct_IsAccepted()
    {
        await WithValidationAsync(async () =>
        {
            var claims = ImpersonationClaims();
            claims.Remove("act");
            var context = CreateContext(Sign(claims), "v1/access/account", new RecordingMachineTokenValidator(StorytellerIssuer) { Owns = true });

            (await InvokeAsync(context)).ShouldBeTrue();

            _middleware.Response.ShouldBeNull();
            context.Items.ContainsKey(FunctionContextItemKeys.CachedClaims).ShouldBeTrue();
            _logger.Entries.ShouldBeEmpty();
        });
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }

    private static Dictionary<string, object> ImpersonationClaims()
    {
        return new Dictionary<string, object>
        {
            ["sub"] = "user_01",
            ["email"] = "ada@example.com",
            ["act"] = new Dictionary<string, object> { ["sub"] = "admin@example.com" },
        };
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

    private TestFunctionContext CreateContext(string token, string route, IMachineTokenValidator machine)
    {
        var user = new AuthKitBearerTokenValidator(
            _options,
            new AuthKitClaimNormalizer(_options),
            new TestHostEnvironment(Environments.Production),
            _logger,
            new StubKeyManager(KeySet()));
        var services = FunctionTestDoubles.CreateServices(user, _options.Value, machine);
        var (context, _) = FunctionTestDoubles.CreateRequest(
            services,
            new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
            url: new Uri($"https://api.example.com/api/{route}"));
        return context;
    }

    private string Sign(Dictionary<string, object> claims)
    {
        // aud is the client id, as the machine validator of a shared issuer requires, so only the user_ sub refuses there.
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = "client_123",
            Claims = claims,
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
