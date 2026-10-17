using System.Net;
using System.Text.Json;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.Certificates.IntegrationTests;

// Federated machines through CosmosAccessService and the policy-aware router with the real federated service,
// and the bootstrap of the token signing key by concurrent instances against the core container.
[Collection(IntegrationTestCollection.Name)]
public class FederatedRoutingTests(CosmosFixture fixture)
{
    private const string Organization = "fedorg";
    private const string Project = "fedrouting";

    [Fact]
    public async Task FederatedMachine_IsCreatedWithItsRules_CannotBeReset_AndIsDeleted()
    {
        var accessService = fixture.Services.GetRequiredService<IAccessService>();
        await EnsureProjectAsync(accessService);
        await fixture.Services.GetRequiredService<IMachineAuthenticationPolicyStore>().SetAsync(Organization, Project, new MachineAuthenticationPolicy
        {
            EnabledCredentialKinds = [MachineCredentialKind.ApiKey, MachineCredentialKind.Federated],
            CredentialKind = MachineCredentialKind.ApiKey,
        });
        await fixture.Services.GetRequiredService<IFederatedIssuerStore>().SetAsync(Organization, Project, new FederatedIssuer
        {
            Name = "github",
            Issuer = "https://token.actions.githubusercontent.com",
            RequiredClaims = new Dictionary<string, IReadOnlyList<string>> { ["repository_owner_id"] = ["1342004"] },
        });
        var rule = new FederationRule { Issuer = "github", Audience = "urn:storyteller:test.platform:api", Subject = "repo:acme/app:*" };

        var created = await accessService.CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = Organization,
            Project = Project,
            Scope = MachineAccessScope.ConfigurationRead,
            CredentialKind = MachineCredentialKind.Federated,
            FederationRules = [rule],
        });

        created.CredentialKind.ShouldBe(MachineCredentialKind.Federated);
        created.AccessKey.ShouldBeEmpty();
        Guid.TryParse(created.Id, out _).ShouldBeTrue();
        var stored = await accessService.GetMachineAccessAsync(Organization, Project, created.Id);
        stored.ShouldNotBeNull();
        stored.CredentialKind.ShouldBe(MachineCredentialKind.Federated);
        stored.FederationRules.ShouldNotBeNull();
        stored.FederationRules.Single().ShouldBe(rule);

        var reset = await Should.ThrowAsync<MachineAccessNotSupportedException>(() => accessService.ResetMachineAccessAsync(Organization, Project, created.Id));
        reset.Message.ShouldBe("A Federated machine has no secret to reset. Change its federation rules instead.");

        (await accessService.DeleteMachineAccessAsync(Organization, Project, created.Id)).ShouldBeTrue();
        (await accessService.GetMachineAccessAsync(Organization, Project, created.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task FederatedMachine_WithARuleOfAnUnknownIssuer_IsRefused()
    {
        var accessService = fixture.Services.GetRequiredService<IAccessService>();
        await EnsureProjectAsync(accessService);
        await fixture.Services.GetRequiredService<IMachineAuthenticationPolicyStore>().SetAsync(Organization, Project, new MachineAuthenticationPolicy
        {
            EnabledCredentialKinds = [MachineCredentialKind.Federated],
        });

        var exception = await Should.ThrowAsync<InvalidInputException>(() => accessService.CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = Organization,
            Project = Project,
            Scope = MachineAccessScope.DefaultRead,
            FederationRules = [new FederationRule { Issuer = "nobody", Audience = "urn:storyteller:test.platform:api", Subject = "sub" }],
        }));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidFederation);
        exception.Message.ShouldBe("Rule 1: the trusted issuer 'nobody' doesn't exist in the project.");
    }

    [Fact]
    public void FederationStores_AreTheCosmosOnes()
    {
        // AddFederatedMachineAccess falls back to in-memory stores, which behave alike; per instance, though, they would
        // give every instance its own signing key and lose the trusted issuers on a restart.
        fixture.Services.GetRequiredService<ITokenSigningKeyStore>().ShouldBeOfType<CosmosTokenSigningKeyStore>();
        fixture.Services.GetRequiredService<IFederatedIssuerStore>().ShouldBeOfType<CosmosFederatedIssuerStore>();
    }

    [Fact]
    public async Task SigningKeyBootstrap_ByConcurrentInstances_EndsWithOneKeyEveryInstanceSignsWith()
    {
        var store = fixture.Services.GetRequiredService<ITokenSigningKeyStore>();
        store.ShouldBeOfType<CosmosTokenSigningKeyStore>();
        var core = fixture.Services.GetRequiredService<IContainerRepositoryProvider>().GetCore().Container;
        using (var deleted = await core.DeleteItemStreamAsync("tsk.1", new PartitionKey("signing-keys")))
        {
            deleted.StatusCode.ShouldBeOneOf(HttpStatusCode.NoContent, HttpStatusCode.NotFound);
        }

        var options = Options.Create(new FederationOptions { Issuer = "urn:storyteller:test.platform", Audience = "urn:storyteller:test.platform:api" });
        var instances = Enumerable.Range(0, 4)
            .Select(_ => new TokenSigningKeyProvider(store, options, TimeProvider.System, NullLogger<TokenSigningKeyProvider>.Instance))
            .ToList();

        var credentials = await Task.WhenAll(instances.Select(instance => Task.Run(instance.GetSigningCredentialsAsync)));

        var stored = await store.GetAsync();
        stored.ShouldNotBeNull();
        credentials.Select(item => item.Key.KeyId).ShouldAllBe(kid => kid == stored.Kid);

        // The key every instance signs with is the document tsk.1 of the core container.
        using (var read = await core.ReadItemStreamAsync("tsk.1", new PartitionKey("signing-keys")))
        {
            read.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var document = await JsonDocument.ParseAsync(read.Content);
            document.RootElement.EnumerateObject()
                .Single(property => string.Equals(property.Name, "kid", StringComparison.OrdinalIgnoreCase))
                .Value.GetString().ShouldBe(stored.Kid);
        }

        // A validating instance that never issued finds the key in the store.
        var validating = new TokenSigningKeyProvider(store, options, TimeProvider.System, NullLogger<TokenSigningKeyProvider>.Instance);
        (await validating.GetValidationKeyAsync(stored.Kid)).ShouldNotBeNull();
    }

    private static async Task EnsureProjectAsync(IAccessService accessService)
    {
        try
        {
            await accessService.CreateAccessPointAsync(new AccessPointCreate { Organization = Organization, Project = Project, OwnerId = "test-owner" });
        }
        catch (ConflictException)
        {
            // Already exists.
        }
    }
}
