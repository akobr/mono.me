using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Api.Security;

using Azure.Core.Serialization;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests.TenantIsolation;

// Two tenants with the same project and view names, so only the organization tells them apart (as the backend's isolation
// suites): A = acme/app/default, where every caller belongs, and B = globex/app/default, which has its own owner and
// machines. The access service has the semantics of the store: an exact, case-sensitive key of the account's access map
// with no inheritance from the organization, and a machine read by organization, project and id. The services the
// middlewares need are real where it counts (the Storyteller token issuer and validator, for federated machines and
// delegated users) and fakes that return A's credentials otherwise. One world serves every test: what a request reached is
// recorded in its own TripwireRecorder.
internal sealed class TenantWorld
{
    public const string OrganizationA = "acme";
    public const string OrganizationB = "globex";
    public const string Project = "app";
    public const string View = "default";

    // Account ids in the form of Entra ID ({tid}:{oid}), the default provider.
    public const string UserA = "8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d:3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2a";
    public const string UserB = "8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d:3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2b";
    public const string ActorId = "mcp";

    public const string ApiKeyMachineId = "7d3f3a52-2c1a-4f3e-9d61-0a6c1f6b9a01";
    public const string CertificateMachineId = "7d3f3a52-2c1a-4f3e-9d61-0a6c1f6b9a02";
    public const string ClientCredentialsMachineId = "client_01JTENANTA0000000000000003";
    public const string FederatedMachineId = "7d3f3a52-2c1a-4f3e-9d61-0a6c1f6b9a04";

    public const string CertificateHeader = "X-ARR-ClientCert";
    public const string Thumbprint = "A1B2C3D4E5F60718293A4B5C6D7E8F9012345678";

    private const string StorytellerIssuer = "https://storyteller.test";
    private const string StorytellerAudience = "https://storyteller.test/api";
    private const string UserToken = "user-a-provider-token";
    private const string ClientCredentialsToken = "client-a-provider-token";

    private static readonly Lazy<Task<TenantWorld>> Shared = new(CreateAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    private TenantWorld(ServiceProvider services, string delegatedToken, string federatedToken, string certificate)
    {
        Services = services;
        DelegatedToken = delegatedToken;
        FederatedToken = federatedToken;
        Certificate = certificate;
    }

    public static IReadOnlyList<MachineScope> AllScopes { get; } = Enum.GetValues<MachineScope>();

    public static IReadOnlyList<TenantCaller> Callers { get; } = Enum.GetValues<TenantCaller>();

    // The services the middlewares read from FunctionContext.InstanceServices.
    public ServiceProvider Services { get; }

    public string DelegatedToken { get; }

    public string FederatedToken { get; }

    public string Certificate { get; }

    public static Task<TenantWorld> GetAsync() => Shared.Value;

    public static bool IsMachine(TenantCaller caller) => caller is not (TenantCaller.User or TenantCaller.DelegatedUser);

    public static string? MachineIdOf(TenantCaller caller)
    {
        return caller switch
        {
            TenantCaller.ApiKeyMachine => ApiKeyMachineId,
            TenantCaller.CertificateMachine => CertificateMachineId,
            TenantCaller.ClientCredentialsMachine => ClientCredentialsMachineId,
            TenantCaller.FederatedMachine => FederatedMachineId,
            _ => null,
        };
    }

    // The credentials as a client presents them.
    public Dictionary<string, string> HeadersOf(TenantCaller caller)
    {
        return caller switch
        {
            TenantCaller.User => new() { ["Authorization"] = $"Bearer {UserToken}" },
            TenantCaller.DelegatedUser => new() { ["Authorization"] = $"Bearer {DelegatedToken}" },
            TenantCaller.ApiKeyMachine => new() { ["Authorization"] = $"ApiKey 2s.{OrganizationA}.{Project}.{ApiKeyMachineId}.secret" },
            TenantCaller.CertificateMachine => new() { [CertificateHeader] = Certificate },
            TenantCaller.ClientCredentialsMachine => new() { ["Authorization"] = $"Bearer {ClientCredentialsToken}" },
            TenantCaller.FederatedMachine => new() { ["Authorization"] = $"Bearer {FederatedToken}" },
            _ => throw new ArgumentOutOfRangeException(nameof(caller), caller, null),
        };
    }

    private static async Task<TenantWorld> CreateAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MachineAuth:Federation:Issuer"] = StorytellerIssuer,
                ["MachineAuth:Federation:Audience"] = StorytellerAudience,
            })
            .Build();

        var services = FunctionTestDoubles.CreateServiceCollection(new ProviderTokens());
        services.AddLogging();
        services.AddFederatedMachineAccess(configuration);
        services.AddSingleton(TenantAccess.Create());
        services.AddSingleton(Options.Create(new TokenExchangeOptions
        {
            Enabled = true,
            Actors = [new TokenExchangeActorOptions { Id = ActorId, SubjectAudiences = ["https://mcp.test/mcp"] }],
        }));
        services.AddSingleton(Options.Create(new MachineAuthenticationOptions
        {
            CertificateHeaderName = CertificateHeader,
            IsCertificateHeaderFromClientAllowed = true,
        }));
        services.AddSingleton<IMachineAuthenticationPolicyStore>(new EveryKindPolicies());
        services.AddSingleton<IApiKeyValidator>(new ApiKeyOfA());
        services.AddSingleton<IClientCertificateValidator>(new CertificateOfA());
        services.AddSingleton(Options.Create(new WorkerOptions { Serializer = new JsonObjectSerializer() }));
        var provider = services.BuildServiceProvider();

        var issuer = provider.GetRequiredService<StorytellerTokenIssuer>();
        var delegated = await issuer.IssueUserAsync(new StorytellerUserTokenRequest
        {
            AccountId = UserA,
            ActorId = ActorId,
            Scopes = [UserScopes.Impersonation],
            Restrictions = [],
            AuthTime = DateTimeOffset.UtcNow,
            MaxLifetime = TimeSpan.FromMinutes(15),
            NotAfter = DateTimeOffset.UtcNow.AddHours(2),
        });
        var federated = await issuer.IssueAsync(new StorytellerTokenRequest
        {
            Organization = OrganizationA,
            Project = Project,
            MachineId = FederatedMachineId,
            Scopes = [.. AllScopes.Select(scope => MachineScopes.ToScopeString(scope)!)],
            FederationIssuer = "ci",
            FederationSubject = "repo:acme/app:ref:refs/heads/main",
        });

        return new TenantWorld(provider, delegated.AccessToken, federated.AccessToken, CreateCertificate());
    }

    private static string CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=acme-app-machine", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        return Convert.ToBase64String(certificate.Export(X509ContentType.Cert));
    }

    private static Claim[] MachineClaims(string machineId)
    {
        return [new Claim("sub", machineId), new Claim("azp", machineId), .. MachineScopeClaims.GetRoleClaims(AllScopes).Select(role => new Claim("roles", role))];
    }

    // The identity provider: the user of A (acting for a user, with a fresh sign-in) and the client-credentials machine of A.
    private sealed class ProviderTokens : IBearerTokenValidator
    {
        public Task<BearerValidationResult?> ValidateAsync(string rawToken, CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
            BearerValidationResult? result = rawToken switch
            {
                UserToken => new BearerValidationResult(
                    [
                        new Claim("sub", UserA),
                        new Claim("name", "Ada of Acme"),
                        new Claim("preferred_username", "ada@acme.test"),
                        new Claim("scp", Scopes.User.Impersonation),
                        new Claim("auth_time", now),
                    ],
                    false,
                    null),
                ClientCredentialsToken => new BearerValidationResult(MachineClaims(ClientCredentialsMachineId), true, ClientCredentialsMachineId),
                _ => null,
            };
            return Task.FromResult(result);
        }
    }

    // The API key of A's API-key machine, whatever key is presented.
    private sealed class ApiKeyOfA : IApiKeyValidator
    {
        public Task<ApiKeyValidationResult?> ValidateAsync(string rawApiKey)
        {
            return Task.FromResult<ApiKeyValidationResult?>(new ApiKeyValidationResult(OrganizationA, Project, ApiKeyMachineId, AllScopes, MachineCredentialKind.ApiKey));
        }
    }

    // The certificate of A's certificate machine, whatever certificate is presented.
    private sealed class CertificateOfA : IClientCertificateValidator
    {
        public Task<ClientCertificateValidationResult?> ValidateAsync(X509Certificate2 clientCertificate)
        {
            return Task.FromResult<ClientCertificateValidationResult?>(new ClientCertificateValidationResult(
                OrganizationA,
                Project,
                CertificateMachineId,
                AllScopes,
                Thumbprint,
                ClientCertificateKind.Machine,
                null,
                MachineCredentialKind.Certificate));
        }
    }

    // Every credential kind on every project, so a refusal can only come from the tenant.
    private sealed class EveryKindPolicies : IMachineAuthenticationPolicyStore
    {
        private static readonly MachineAuthenticationPolicy Policy = new() { EnabledCredentialKinds = Enum.GetValues<MachineCredentialKind>() };

        public Task<MachineAuthenticationPolicy?> GetAsync(string organization, string project)
        {
            return Task.FromResult<MachineAuthenticationPolicy?>(Policy);
        }

        public Task SetAsync(string organization, string project, MachineAuthenticationPolicy policy, AccessCaller caller)
        {
            throw TripwireRecorder.Trip(nameof(IMachineAuthenticationPolicyStore), typeof(IMachineAuthenticationPolicyStore).GetMethod(nameof(SetAsync))!, [organization, project, policy, caller]);
        }
    }
}
