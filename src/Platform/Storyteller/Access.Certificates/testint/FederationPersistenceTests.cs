using System.Security.Cryptography;
using System.Text.Json;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace _42.Platform.Storyteller.Access.Certificates.IntegrationTests;

// Trusted issuers, federation rules and the token signing key against real storage: written by the Cosmos SDK
// (Newtonsoft), read by point reads (System.Text.Json) and by queries (Newtonsoft).
[Collection(IntegrationTestCollection.Name)]
public class FederationPersistenceTests(CosmosFixture fixture)
{
    private const string Organization = "fedorg";

    private static readonly IReadOnlyList<FederationRule> Rules =
    [
        new FederationRule
        {
            Issuer = "github",
            Audience = "urn:storyteller:test.platform:api",
            Subject = "repo:acme/app:*",
            Claims = new Dictionary<string, IReadOnlyList<string>>
            {
                ["repository_owner_id"] = ["1342004"],
                ["environment"] = ["prod", "staging"],
            },
        },
        new FederationRule
        {
            Issuer = "k8s",
            Audience = "urn:storyteller:test.platform:api",
            Subject = "system:serviceaccount:prod:app",
            Claims = new Dictionary<string, IReadOnlyList<string>> { ["/kubernetes.io/namespace"] = ["prod"] },
        },
    ];

    [Fact]
    public async Task IssuerStore_RoundTripsAnIssuerAndKeepsItsIssuer()
    {
        const string project = "issuers";
        await EnsureProjectAsync(project);
        var store = CreateIssuerStore();
        var issuer = new FederatedIssuer
        {
            Name = "k8s",
            Issuer = "https://kubernetes.default.svc.cluster.local",
            KeySource = FederatedIssuerKeySource.Jwks,
            Jwks = """{"keys":[{"kty":"EC","kid":"ec-1","crv":"P-256","x":"f83OJ3D2xF1Bg8vub9tLe1gHMzV76e8Tus9uPHvRVEU","y":"x_FEzRu9m36HLN_tue659LNpXW6pCyStikYjKIWI5a0"}]}""",
            MaxTokenLifetimeSeconds = 7200,
            RequiredClaims = new Dictionary<string, IReadOnlyList<string>>
            {
                ["/kubernetes.io/namespace"] = ["prod", "staging"],
                ["/https:~1~1sts.amazonaws.com~1/aws_account"] = ["123456789012"],
            },
            Description = "Production cluster",
        };

        await store.SetAsync(Organization, project, issuer);

        var stored = await store.GetAsync(Organization, project, "k8s");
        stored.ShouldNotBeNull();
        stored.Issuer.ShouldBe(issuer.Issuer);
        stored.KeySource.ShouldBe(FederatedIssuerKeySource.Jwks);
        stored.Jwks.ShouldBe(issuer.Jwks);
        stored.MaxTokenLifetimeSeconds.ShouldBe(7200);
        stored.Description.ShouldBe("Production cluster");
        stored.RequiredClaims.ShouldNotBeNull();
        stored.RequiredClaims["/kubernetes.io/namespace"].ShouldBe(["prod", "staging"]);
        stored.RequiredClaims["/https:~1~1sts.amazonaws.com~1/aws_account"].ShouldBe(["123456789012"]);
        (await store.ListAsync(Organization, project)).Select(item => item.Name).ShouldBe(["k8s"]);

        await store.SetAsync(Organization, project, issuer with { Description = "Renamed", RequiredClaims = null });
        var updated = await store.GetAsync(Organization, project, "k8s");
        updated!.Description.ShouldBe("Renamed");
        updated.RequiredClaims.ShouldBeNull();

        var changed = await Should.ThrowAsync<InvalidInputException>(
            () => store.SetAsync(Organization, project, issuer with { Issuer = "https://kubernetes.default.svc.cluster.local/" }));
        changed.Message.ShouldBe(FederationValidation.IssuerImmutableMessage);
        changed.ErrorCode.ShouldBe(ErrorCodes.InvalidFederation);

        (await store.DeleteAsync(Organization, project, "k8s")).ShouldBeTrue();
        (await store.DeleteAsync(Organization, project, "k8s")).ShouldBeFalse();
        (await store.GetAsync(Organization, project, "k8s")).ShouldBeNull();
        (await store.ListAsync(Organization, project)).ShouldBeEmpty();
    }

    [Fact]
    public async Task IssuerStore_ListIsCachedPerInstanceAndDroppedByItsOwnWrites()
    {
        const string project = "issuercache";
        await EnsureProjectAsync(project);
        var first = CreateIssuerStore();
        var second = CreateIssuerStore();

        (await first.ListAsync(Organization, project)).ShouldBeEmpty();

        await second.SetAsync(Organization, project, Issuer("b"));

        (await first.ListAsync(Organization, project)).ShouldBeEmpty();
        (await second.ListAsync(Organization, project)).Select(item => item.Name).ShouldBe(["b"]);

        await first.SetAsync(Organization, project, Issuer("a"));

        (await first.ListAsync(Organization, project)).Select(item => item.Name).ShouldBe(["a", "b"]);
    }

    [Fact]
    public async Task IssuerStore_RefusesTheIssuerNumber21()
    {
        const string project = "issuerlimit";
        await EnsureProjectAsync(project);
        var store = CreateIssuerStore();

        for (var index = 1; index <= FederationValidation.MaxIssuersPerProject; index++)
        {
            await store.SetAsync(Organization, project, Issuer($"issuer-{index}"));
        }

        var refused = await Should.ThrowAsync<InvalidInputException>(() => store.SetAsync(Organization, project, Issuer("issuer-21")));
        refused.Message.ShouldBe(FederationValidation.TooManyIssuersMessage);

        // Replacing an existing issuer needs no room.
        await store.SetAsync(Organization, project, Issuer("issuer-20") with { Description = "kept" });
        (await store.ListAsync(Organization, project)).Count.ShouldBe(FederationValidation.MaxIssuersPerProject);
    }

    [Fact]
    public async Task IssuerStore_ListsNothingForAnUnknownOrganization()
    {
        (await CreateIssuerStore().ListAsync("fedunknown", "nothing")).ShouldBeEmpty();
    }

    [Fact]
    public async Task FederationRules_AreStoredOnCreateAndReadByGetAndList()
    {
        const string project = "fedrules";
        await EnsureProjectAsync(project);
        var machineId = Guid.NewGuid().ToString("D");
        var accessService = CreateAccessService(machineId);

        var created = await accessService.CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = Organization,
            Project = project,
            Scope = MachineAccessScope.DefaultRead,
            CredentialKind = MachineCredentialKind.Federated,
            FederationRules = Rules,
        });

        created.FederationRules.ShouldNotBeNull();

        var read = await accessService.GetMachineAccessAsync(Organization, project, machineId);
        ShouldMatchRules(read!);

        var listed = (await accessService.GetMachineAccessesAsync(Organization, project)).Single(access => access.Id == machineId);
        ShouldMatchRules(listed);
    }

    [Fact]
    public async Task SetMachineFederationRules_ReplacesTheRulesOfAFederatedMachineOnly()
    {
        const string project = "fedreplace";
        await EnsureProjectAsync(project);
        var machineId = Guid.NewGuid().ToString("D");
        var accessService = CreateAccessService(machineId);
        await accessService.CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = Organization,
            Project = project,
            Scope = MachineAccessScope.DefaultRead,
            CredentialKind = MachineCredentialKind.Federated,
            FederationRules = [Rules[1]],
        });

        var updated = await accessService.SetMachineFederationRulesAsync(Organization, project, machineId, Rules);

        updated.Id.ShouldBe(machineId);
        ShouldMatchRules(updated);
        ShouldMatchRules((await accessService.GetMachineAccessAsync(Organization, project, machineId))!);

        await Should.ThrowAsync<NotFoundException>(
            () => accessService.SetMachineFederationRulesAsync(Organization, project, Guid.NewGuid().ToString("D"), Rules));

        var apiKeyMachine = await fixture.Services.GetRequiredService<IAccessService>().CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = Organization,
            Project = project,
            Scope = MachineAccessScope.DefaultRead,
            CredentialKind = MachineCredentialKind.ApiKey,
        });
        var refused = await Should.ThrowAsync<InvalidInputException>(
            () => accessService.SetMachineFederationRulesAsync(Organization, project, apiKeyMachine.Id, Rules));
        refused.ErrorCode.ShouldBe(ErrorCodes.MachineNotFederated);
    }

    [Fact]
    public async Task FindMachinesUsingIssuer_ReturnsUpToMaxMachinesReferencingTheName()
    {
        const string project = "fedfind";
        await EnsureProjectAsync(project);
        var ids = new List<string>();

        for (var index = 0; index < 3; index++)
        {
            var machineId = Guid.NewGuid().ToString("D");
            ids.Add(machineId);
            await CreateAccessService(machineId).CreateMachineAccessAsync(new MachineAccessCreate
            {
                Organization = Organization,
                Project = project,
                Scope = MachineAccessScope.DefaultRead,
                CredentialKind = MachineCredentialKind.Federated,
                FederationRules = index == 0 ? [Rules[1]] : Rules,
            });
        }

        var accessService = fixture.Services.GetRequiredService<IAccessService>();

        (await accessService.FindMachinesUsingIssuerAsync(Organization, project, "k8s", 10)).ShouldBe(ids, ignoreOrder: true);
        (await accessService.FindMachinesUsingIssuerAsync(Organization, project, "github", 10)).ShouldBe(ids.Skip(1), ignoreOrder: true);
        (await accessService.FindMachinesUsingIssuerAsync(Organization, project, "k8s", 2)).Count.ShouldBe(2);
        (await accessService.FindMachinesUsingIssuerAsync(Organization, project, "gone", 10)).ShouldBeEmpty();
        (await accessService.FindMachinesUsingIssuerAsync("fedunknown", project, "k8s", 10)).ShouldBeEmpty();
    }

    [Fact]
    public async Task SigningKeyStore_KeepsTheFirstKey()
    {
        var store = fixture.Services.GetRequiredService<ITokenSigningKeyStore>();
        var first = NewKey("kid-first");

        var created = await store.TryCreateAsync(first);
        var stored = await store.GetAsync();

        stored.ShouldNotBeNull();

        if (created)
        {
            stored.Kid.ShouldBe(first.Kid);
            stored.Algorithm.ShouldBe("RS256");
            stored.PrivateKeyPkcs8.ShouldBe(first.PrivateKeyPkcs8);
        }

        (await store.TryCreateAsync(NewKey("kid-second"))).ShouldBeFalse();
        (await store.GetAsync())!.Kid.ShouldBe(stored.Kid);
    }

    private static FederatedIssuer Issuer(string name)
    {
        return new FederatedIssuer { Name = name, Issuer = $"https://{name}.oidc.example.com" };
    }

    private static TokenSigningKey NewKey(string kid)
    {
        using var rsa = RSA.Create(2048);
        return new TokenSigningKey(kid, "RS256", rsa.ExportPkcs8PrivateKey(), DateTimeOffset.UtcNow);
    }

    private static void ShouldMatchRules(MachineAccess access)
    {
        access.CredentialKind.ShouldBe(MachineCredentialKind.Federated);
        access.FederationRules.ShouldNotBeNull();
        access.FederationRules.Count.ShouldBe(2);
        access.FederationRules[0].Issuer.ShouldBe("github");
        access.FederationRules[0].Subject.ShouldBe("repo:acme/app:*");
        access.FederationRules[0].Claims!["environment"].ShouldBe(["prod", "staging"]);
        access.FederationRules[0].Claims!["repository_owner_id"].ShouldBe(["1342004"]);
        access.FederationRules[1].Claims!["/kubernetes.io/namespace"].ShouldBe(["prod"]);
    }

    private CosmosFederatedIssuerStore CreateIssuerStore()
    {
        return new CosmosFederatedIssuerStore(
            fixture.Services.GetRequiredService<IContainerRepositoryProvider>(),
            fixture.Services.GetRequiredService<IOptions<JsonSerializerOptions>>(),
            new MemoryCache(new MemoryCacheOptions()));
    }

    // The Federated arm of the machine router is not part of this layer: a stub returns what it would.
    private CosmosAccessService CreateAccessService(string machineId)
    {
        var router = new Mock<IMachineAccessService>();
        router
            .Setup(service => service.CreateMachineAccessAsync(It.IsAny<MachineAccessCreate>()))
            .ReturnsAsync((MachineAccessCreate model) => new MachineAccess
            {
                Id = machineId,
                ObjectId = machineId,
                AccessKey = string.Empty,
                Scope = model.Scope,
                CredentialKind = MachineCredentialKind.Federated,
                FederationRules = model.FederationRules,
            });

        return new CosmosAccessService(
            fixture.Services.GetRequiredService<IContainerRepositoryProvider>(),
            fixture.Services.GetRequiredService<IContainerFactory>(),
            fixture.Services.GetRequiredService<IOptions<JsonSerializerOptions>>(),
            router.Object);
    }

    private async Task EnsureProjectAsync(string project)
    {
        try
        {
            await fixture.Services.GetRequiredService<IAccessService>()
                .CreateAccessPointAsync(new AccessPointCreate { Organization = Organization, Project = project, OwnerId = "test-owner" });
        }
        catch (ConflictException)
        {
            // Already exists.
        }
    }
}
