using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace _42.Platform.Storyteller.Access.Certificates.UnitTests;

// The access service stores the machine the router returns, and the credentials carry what they recorded at creation:
// every kind returns exactly the requested scopes, and every credential record keeps them.
public class PolicyAwareScopesTests
{
    private static readonly DateTimeOffset NotAfter = new(2027, 1, 12, 0, 0, 0, TimeSpan.Zero);

    // Runtime and management scopes in one list, as the API completed it: nothing on the way adds or drops one.
    private static readonly MachineScope[] Granted =
    [
        MachineScope.ConfigurationRead,
        MachineScope.ConfigurationWrite,
        MachineScope.SecretsRead,
        MachineScope.ConfigurationTransfer,
        MachineScope.KeyValueManage,
    ];

    private readonly Mock<IMachineAuthenticationPolicyStore> _policyStore = new();
    private readonly InMemoryApiKeyHashStore _hashStore = new();
    private readonly Mock<IClientCertificateAuthority> _authority = new();
    private readonly Mock<IClientCertificateStore> _certificateStore = new();
    private readonly Mock<IIdentityProviderMachineAccessService> _identityProvider = new();
    private readonly Mock<IFederatedMachineAccessService> _federated = new();
    private readonly List<CertificateRecord> _records = [];
    private readonly List<MachineAccessCreate> _forwarded = [];

    public PolicyAwareScopesTests()
    {
        _policyStore.Setup(store => store.GetAsync("org1", "proj1")).ReturnsAsync(new MachineAuthenticationPolicy
        {
            EnabledCredentialKinds =
            [
                MachineCredentialKind.ApiKey,
                MachineCredentialKind.Certificate,
                MachineCredentialKind.CertificateAndApiKey,
                MachineCredentialKind.ClientCredentials,
                MachineCredentialKind.Federated,
            ],
        });
        _authority
            .Setup(authority => authority.IssueCertificateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new IssuedCertificate([1, 2, 3], "password", "THUMBPRINT", "01", NotAfter.AddDays(-90), NotAfter));
        _certificateStore
            .Setup(store => store.StoreAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CertificateRecord>()))
            .Callback((string _, string _, CertificateRecord record) => _records.Add(record))
            .Returns(Task.CompletedTask);

        // Like the real services, the doubles return what the model asked for.
        _identityProvider.Setup(service => service.CreateMachineAccessAsync(It.IsAny<MachineAccessCreate>()))
            .Callback((MachineAccessCreate model) => _forwarded.Add(model))
            .ReturnsAsync((MachineAccessCreate model) => FromModel(model));
        _federated.Setup(service => service.CreateMachineAccessAsync(It.IsAny<MachineAccessCreate>()))
            .Callback((MachineAccessCreate model) => _forwarded.Add(model))
            .ReturnsAsync((MachineAccessCreate model) => FromModel(model));
    }

    [Theory]
    [InlineData(MachineCredentialKind.ApiKey)]
    [InlineData(MachineCredentialKind.Certificate)]
    [InlineData(MachineCredentialKind.CertificateAndApiKey)]
    [InlineData(MachineCredentialKind.ClientCredentials)]
    [InlineData(MachineCredentialKind.Federated)]
    public async Task Create_EveryKind_ReturnsTheRequestedScopes(MachineCredentialKind kind)
    {
        var access = await CreateService().CreateMachineAccessAsync(Model(kind, Granted));

        access.CredentialKind.ShouldBe(kind);
        access.Scopes.ShouldBe(Granted);
    }

    [Theory]
    [InlineData(MachineCredentialKind.ClientCredentials)]
    [InlineData(MachineCredentialKind.Federated)]
    public async Task Create_TheServiceOfTheKind_GetsTheScopes(MachineCredentialKind kind)
    {
        await CreateService().CreateMachineAccessAsync(Model(kind, Granted));

        _forwarded.ShouldHaveSingleItem().Scopes.ShouldBe(Granted);
    }

    [Theory]
    [InlineData(MachineCredentialKind.Certificate)]
    [InlineData(MachineCredentialKind.CertificateAndApiKey)]
    public async Task Create_TheCertificateRecord_CarriesTheScopes(MachineCredentialKind kind)
    {
        await CreateService().CreateMachineAccessAsync(Model(kind, Granted));

        var record = _records.ShouldHaveSingleItem();
        record.CredentialKind.ShouldBe(kind);
        record.Scopes.ShouldBe(Granted);
    }

    [Theory]
    [InlineData(MachineCredentialKind.ApiKey, false)]
    [InlineData(MachineCredentialKind.CertificateAndApiKey, false)]
    [InlineData(MachineCredentialKind.CertificateAndApiKey, true)]
    public async Task Create_TheApiKey_CarriesTheScopes(MachineCredentialKind kind, bool useSharedCertificate)
    {
        var access = await CreateService().CreateMachineAccessAsync(Model(kind, Granted) with { UseSharedCertificate = useSharedCertificate });

        access.Scopes.ShouldBe(Granted);
        _hashStore.Entries.ShouldHaveSingleItem().Value.Scopes.ShouldBe(Granted);
        (await _hashStore.Validator.ValidateAsync(access.AccessKey)).ShouldNotBeNull().Scopes.ShouldBe(Granted);
    }

    // The API refuses a machine without a runtime scope; a missing list still reaches no record as null.
    [Theory]
    [InlineData(MachineCredentialKind.ApiKey)]
    [InlineData(MachineCredentialKind.Certificate)]
    [InlineData(MachineCredentialKind.CertificateAndApiKey)]
    public async Task Create_WithoutScopes_RecordsAnEmptyList(MachineCredentialKind kind)
    {
        var access = await CreateService().CreateMachineAccessAsync(Model(kind, null));

        access.Scopes.ShouldBeEmpty();
        _records.ShouldAllBe(record => record.Scopes.Count == 0);
        _hashStore.Entries.Values.ShouldAllBe(entry => entry.Scopes.Count == 0);
    }

    // A new secret keeps what the machine was granted, and the new key carries it.
    [Fact]
    public async Task Reset_TheApiKey_KeepsTheScopes()
    {
        var service = CreateService();
        var access = await service.CreateMachineAccessAsync(Model(MachineCredentialKind.ApiKey, Granted));

        var newKey = await service.ResetMachineAccessAsync(access, "org1", "proj1");

        newKey.ShouldNotBeNull();
        var validated = await _hashStore.Validator.ValidateAsync(newKey);
        validated.ShouldNotBeNull();
        validated.Scopes.ShouldBe(Granted);
        (await _hashStore.Validator.ValidateAsync(access.AccessKey)).ShouldBeNull();
    }

    private static MachineAccessCreate Model(MachineCredentialKind kind, IReadOnlyList<MachineScope>? scopes)
    {
        return new MachineAccessCreate
        {
            Organization = "org1",
            Project = "proj1",
            Scopes = scopes,
            CredentialKind = kind,
            FederationRules = kind == MachineCredentialKind.Federated
                ? [new FederationRule { Issuer = "github", Audience = "urn:test:storyteller", Subject = "repo:acme/app:ref:refs/heads/main" }]
                : null,
        };
    }

    private static MachineAccess FromModel(MachineAccessCreate model)
    {
        return new MachineAccess
        {
            Id = "3b0f9d3e-8f43-4a8e-9a0c-2f7d3c1e5b6a",
            ObjectId = "3b0f9d3e-8f43-4a8e-9a0c-2f7d3c1e5b6a",
            AccessKey = string.Empty,
            Scopes = model.Scopes ?? [],
        };
    }

    private PolicyAwareMachineAccessService CreateService()
    {
        var options = Options.Create(TestCertificateHelper.DefaultOptions);

        return new PolicyAwareMachineAccessService(
            new ApiKeyMachineAccessService(_hashStore),
            new CertificateMachineAccessService(_authority.Object, _certificateStore.Object, options, NullLogger<CertificateMachineAccessService>.Instance),
            _policyStore.Object,
            options,
            NullLogger<PolicyAwareMachineAccessService>.Instance,
            _identityProvider.Object,
            _federated.Object);
    }

    private sealed class InMemoryApiKeyHashStore : IApiKeyHashStore
    {
        public Dictionary<string, ApiKeyHashEntry> Entries { get; } = [];

        public IApiKeyValidator Validator => new ApiKeyMachineAccessService(this);

        public Task StoreAsync(
            string organization,
            string project,
            string machineAccessId,
            string hashedSecret,
            IReadOnlyList<MachineScope> scopes)
        {
            Entries[$"{organization}.{project}.{machineAccessId}"] = new ApiKeyHashEntry(hashedSecret, scopes);
            return Task.CompletedTask;
        }

        public Task<ApiKeyHashEntry?> GetAsync(string organization, string project, string machineAccessId)
        {
            return Task.FromResult(Entries.GetValueOrDefault($"{organization}.{project}.{machineAccessId}"));
        }

        public Task<bool> DeleteAsync(string organization, string project, string machineAccessId)
        {
            return Task.FromResult(Entries.Remove($"{organization}.{project}.{machineAccessId}"));
        }
    }
}
