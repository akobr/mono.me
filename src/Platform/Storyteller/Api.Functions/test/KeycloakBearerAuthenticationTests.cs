using System.Net;
using System.Security.Cryptography;
using System.Text;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api;
using _42.Platform.Storyteller.Api.Security;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// Keycloak users and Keycloak machines may share the realm. The user validator is asked first and refuses service
// accounts; the machine validator accepts only service-account tokens of the realm. So a person is a person or 401,
// never a machine, and a service account is the machine of its client.
// BearerValidationMode.DecodeWithoutValidation is static: the classes that switch it never run in parallel.
[Collection(nameof(BearerValidationMode))]
public sealed class KeycloakBearerAuthenticationTests : IDisposable
{
    private const string Issuer = "https://keycloak.example/realms/storyteller";
    private const string Audience = "storyteller";
    private const string UserId = "5f1c2b3a-9d8e-4f70-8a6b-1c2d3e4f5a6b";
    private const string MachineId = "42.sform.org1.proj1.abc";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;
    private readonly IOptions<UserAuthenticationOptions> _options;
    private readonly RecordingMiddleware _middleware = new();

    public KeycloakBearerAuthenticationTests()
    {
        _key = new RsaSecurityKey(_rsa) { KeyId = "realm-key" };
        _options = Options.Create(new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.Keycloak,
            Keycloak = new KeycloakUserOptions
            {
                Issuer = Issuer,
                Audience = Audience,
                AllowedClientIds = ["sform"],
                DefaultUserScopes = [Scopes.User.Impersonation],
            },
        });
    }

    [Fact]
    public async Task UserTokenOfTheRealm_IsAUserKeyedBySub_AndTheMachineValidatorIsNotAsked()
    {
        await WithModeAsync(decodeWithoutValidation: false, async () =>
        {
            var machines = new CountingConfigurationManager(Configuration());
            var token = Sign(new Dictionary<string, object>
            {
                ["sub"] = UserId,
                ["azp"] = "sform",
                ["typ"] = "Bearer",
                ["preferred_username"] = "ada",
            });
            var context = CreateContext(token, machines);

            (await InvokeAsync(context)).ShouldBeTrue();

            var request = (await context.GetHttpRequestDataAsync()).ShouldNotBeNull();
            context.Items.ContainsKey(FunctionContextItemKeys.MachineIdentity).ShouldBeFalse();
            request.GetIdentityUniqueId().ShouldBe(UserId);
            request.GetScopes().ShouldBe([Scopes.User.Impersonation]);
            machines.Calls.ShouldBe(0);
        });
    }

    [Fact]
    public async Task ServiceAccountTokenOfTheRealm_IsRefusedAsAUser_AndIsTheMachineOfItsClient()
    {
        await WithModeAsync(decodeWithoutValidation: false, async () =>
        {
            var machines = new CountingConfigurationManager(Configuration());
            var token = Sign(new Dictionary<string, object>
            {
                ["sub"] = "b0c1d2e3-service-account",
                ["azp"] = MachineId,
                ["client_id"] = MachineId,
                ["preferred_username"] = $"service-account-{MachineId}",
                ["storyteller_scope"] = Scopes.Annotation.Read,
            });
            var context = CreateContext(token, machines);

            (await InvokeAsync(context)).ShouldBeTrue();

            var request = (await context.GetHttpRequestDataAsync()).ShouldNotBeNull();
            context.Items[FunctionContextItemKeys.MachineIdentity].ShouldBe(MachineId);
            request.GetScopes().ShouldBe([Scopes.Annotation.Read]);
            machines.Calls.ShouldBe(1);
        });
    }

    // The user validator refuses the client; the machine validator is asked and refuses a token that is not a service
    // account's, so the person does not become the machine of the client.
    [Fact]
    public async Task UserTokenOfAClientOutsideAllowedClientIds_IsUnauthorized_AndNotAMachine()
    {
        await WithModeAsync(decodeWithoutValidation: false, async () =>
        {
            var machines = new CountingConfigurationManager(Configuration());
            var token = Sign(new Dictionary<string, object>
            {
                ["sub"] = UserId,
                ["azp"] = "admin-ui",
                ["typ"] = "Bearer",
                ["preferred_username"] = "ada",
            });
            var context = CreateContext(token, machines);

            (await InvokeAsync(context)).ShouldBeFalse();

            _middleware.Response.ShouldNotBeNull().StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            context.Items.ContainsKey(FunctionContextItemKeys.MachineIdentity).ShouldBeFalse();
            machines.Calls.ShouldBe(1);
        });
    }

    [Fact]
    public async Task IdTokenOfTheRealm_IsUnauthorized()
    {
        await WithModeAsync(decodeWithoutValidation: false, async () =>
        {
            // The user validator refuses it by its typ, the machine validator because it is not a service account's.
            var machines = new CountingConfigurationManager(Configuration());
            var context = CreateContext(
                Sign(new Dictionary<string, object> { ["sub"] = UserId, ["azp"] = "storyteller", ["typ"] = "ID" }),
                machines);

            (await InvokeAsync(context)).ShouldBeFalse();

            _middleware.Response.ShouldNotBeNull().StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            context.Items.ContainsKey(FunctionContextItemKeys.MachineIdentity).ShouldBeFalse();
            machines.Calls.ShouldBe(1);
        });
    }

    // DEV_AUTH decodes without validation; the Keycloak normalizer still tells service accounts from people.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecodedToken_UnderDebugDecoding_IsAPersonOrTheMachineOfItsServiceAccount(bool serviceAccount)
    {
        await WithModeAsync(decodeWithoutValidation: true, async () =>
        {
            var payload = serviceAccount
                ? $$"""{"iss":"{{Issuer}}","sub":"b0c1d2e3","azp":"{{MachineId}}","client_id":"{{MachineId}}"}"""
                : $$$"""{"iss":"{{{Issuer}}}","sub":"{{{UserId}}}","azp":"sform","realm_access":{"roles":["reader"]}}""";
            var context = CreateContext(UnsignedJwt(payload), new CountingConfigurationManager(Configuration()));

            (await InvokeAsync(context)).ShouldBeTrue();

            var request = (await context.GetHttpRequestDataAsync()).ShouldNotBeNull();

            if (serviceAccount)
            {
                context.Items[FunctionContextItemKeys.MachineIdentity].ShouldBe(MachineId);
            }
            else
            {
                context.Items.ContainsKey(FunctionContextItemKeys.MachineIdentity).ShouldBeFalse();
                request.GetIdentityUniqueId().ShouldBe(UserId);
                request.GetScopes().ShouldBe([Scopes.User.Impersonation]);
            }
        });
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }

    private static async Task WithModeAsync(bool decodeWithoutValidation, Func<Task> body)
    {
        var previous = BearerValidationMode.DecodeWithoutValidation;
        BearerValidationMode.DecodeWithoutValidation = decodeWithoutValidation;

        try
        {
            await body();
        }
        finally
        {
            BearerValidationMode.DecodeWithoutValidation = previous;
        }
    }

    private static string UnsignedJwt(string payloadJson)
    {
        return string.Join('.', Base64Url("""{"alg":"none","typ":"JWT"}"""), Base64Url(payloadJson), "x");
    }

    private static string Base64Url(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private OpenIdConnectConfiguration Configuration()
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
        configuration.SigningKeys.Add(_key);
        return configuration;
    }

    // The user validator and the machine validator of one realm, each with its own key source as in a deployment.
    private TestFunctionContext CreateContext(string token, CountingConfigurationManager machineKeys)
    {
        var user = new KeycloakBearerTokenValidator(_options, new KeycloakClaimNormalizer(_options), new CountingConfigurationManager(Configuration()));
        var machine = new KeycloakMachineTokenValidator(
            Options.Create(new KeycloakOptions { ServerUrl = "https://keycloak.example", Realm = "storyteller", Audience = Audience }),
            machineKeys);
        var services = FunctionTestDoubles.CreateServices(user, _options.Value, machine);
        var (context, _) = FunctionTestDoubles.CreateRequest(services, new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" });
        return context;
    }

    private string Sign(Dictionary<string, object> claims)
    {
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Claims = claims,
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
        });
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

    private sealed class CountingConfigurationManager(OpenIdConnectConfiguration configuration) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public int Calls { get; private set; }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        {
            Calls++;
            return Task.FromResult(configuration);
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
