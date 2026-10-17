using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;

namespace _42.Platform.Storyteller.Access.Federation.UnitTests;

public class FederatedMachineAccessServiceTests
{
    private readonly InMemoryFederatedIssuerStore _issuers = new();
    private readonly FederatedMachineAccessService _service;

    public FederatedMachineAccessServiceTests()
    {
        _service = new FederatedMachineAccessService(_issuers);
        _issuers.SetAsync("org1", "proj1", new FederatedIssuer
        {
            Name = "cluster",
            Issuer = "https://oidc.prod-aks.azure.com/tenant/issuer/",
            KeySource = FederatedIssuerKeySource.Discovery,
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Create_WithValidRules_IsAFederatedMachineWithoutASecret()
    {
        var rules = new List<FederationRule> { Rule() };

        var access = await _service.CreateMachineAccessAsync(Model(rules));

        Guid.TryParse(access.Id, out _).ShouldBeTrue();
        access.ObjectId.ShouldBe(access.Id);
        access.AccessKey.ShouldBeEmpty();
        access.Scope.ShouldBe(MachineAccessScope.ConfigurationRead);
        access.AnnotationKey.ShouldBe("unit.service");
        access.CredentialKind.ShouldBe(MachineCredentialKind.Federated);
        access.FederationRules.ShouldBe(rules);
        access.TokenScope.ShouldBeNull();
        access.Certificate.ShouldBeNull();
    }

    [Fact]
    public async Task Create_WithoutRules_IsRefused()
    {
        var exception = await Should.ThrowAsync<InvalidInputException>(() => _service.CreateMachineAccessAsync(Model(null)));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidFederation);
        exception.Message.ShouldBe("A Federated machine needs at least one federation rule.");
    }

    [Theory]
    [InlineData("other", "urn:test:storyteller", "Rule 1: the trusted issuer 'other' doesn't exist in the project.")]
    [InlineData("cluster", "sts.amazonaws.com", "Rule 1: the audience sts.amazonaws.com belongs to another service.")]
    [InlineData("cluster", "https://oidc.prod-aks.azure.com/tenant/issuer", "Rule 1: the audience https://oidc.prod-aks.azure.com/tenant/issuer is the issuer itself, the default audience of its tokens for other services.")]
    public async Task Create_WithAnInvalidRule_IsRefusedWithTheValidationMessage(string issuer, string audience, string message)
    {
        var exception = await Should.ThrowAsync<InvalidInputException>(
            () => _service.CreateMachineAccessAsync(Model([Rule() with { Issuer = issuer, Audience = audience }])));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidFederation);
        exception.Message.ShouldBe(message);
    }

    [Fact]
    public async Task Create_ValidatesAgainstTheIssuersOfItsOwnProject()
    {
        var exception = await Should.ThrowAsync<InvalidInputException>(
            () => _service.CreateMachineAccessAsync(Model([Rule()]) with { Project = "proj2" }));

        exception.Message.ShouldBe("Rule 1: the trusted issuer 'cluster' doesn't exist in the project.");
    }

    [Fact]
    public async Task Reset_IsNotSupported()
    {
        var access = await _service.CreateMachineAccessAsync(Model([Rule()]));

        var exception = await Should.ThrowAsync<MachineAccessNotSupportedException>(() => _service.ResetMachineAccessAsync(access, "org1", "proj1"));
        exception.Message.ShouldBe("A Federated machine has no secret to reset. Change its federation rules instead.");
        await Should.ThrowAsync<MachineAccessNotSupportedException>(() => _service.ResetMachineAccessAsync(access.Id, "org1", "proj1"));
    }

    [Fact]
    public async Task Delete_HasNothingExternalToRemove()
    {
        var access = await _service.CreateMachineAccessAsync(Model([Rule()]));

        (await _service.DeleteMachineAccessAsync(access, "org1", "proj1")).ShouldBeTrue();
        (await _service.DeleteMachineAccessAsync(access.ObjectId, "org1", "proj1")).ShouldBeTrue();
    }

    private static FederationRule Rule()
    {
        return new FederationRule
        {
            Issuer = "cluster",
            Audience = "urn:test:storyteller",
            Subject = "system:serviceaccount:prod:app",
            Claims = new Dictionary<string, IReadOnlyList<string>> { ["/kubernetes.io/namespace"] = ["prod"] },
        };
    }

    private static MachineAccessCreate Model(IReadOnlyList<FederationRule>? rules)
    {
        return new MachineAccessCreate
        {
            Organization = "org1",
            Project = "proj1",
            Scope = MachineAccessScope.ConfigurationRead,
            AnnotationKey = "unit.service",
            CredentialKind = MachineCredentialKind.Federated,
            FederationRules = rules,
        };
    }
}
