using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Access.Certificates.IntegrationTests;

/// <summary>
/// ClientCredentials machine access through CosmosAccessService and the policy-aware router,
/// with a recording identity provider in place of WorkOS.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class ClientCredentialsRoutingTests(CosmosFixture fixture)
{
    private const string Organization = "testorg";
    private const string Project = "m2mproj";

    [Fact]
    public async Task ClientCredentialsMachine_IsStoredThenResetAndDeletedByItsStoredKind()
    {
        var accessService = fixture.Services.GetRequiredService<IAccessService>();
        var policyStore = fixture.Services.GetRequiredService<IMachineAuthenticationPolicyStore>();
        var identityProvider = fixture.Services.GetRequiredService<RecordingIdentityProviderMachineAccessService>();

        try
        {
            await accessService.CreateAccessPointAsync(new AccessPointCreate { Organization = Organization, Project = Project, OwnerId = "test-owner" });
        }
        catch
        {
            // Already exists — fine.
        }

        await policyStore.SetAsync(Organization, Project, new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.ClientCredentials });

        var created = await accessService.CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = Organization,
            Project = Project,
            Scope = MachineAccessScope.DefaultRead,
        });

        created.CredentialKind.ShouldBe(MachineCredentialKind.ClientCredentials);
        created.Id.ShouldStartWith("client_");
        created.AccessKey.ShouldStartWith("secret-");
        (await accessService.VerifyAccessForMachineAsync(Organization, Project, created.Id)).ShouldBeTrue();

        var stored = (await accessService.GetMachineAccessesAsync(Organization, Project)).Single(access => access.Id == created.Id);
        stored.CredentialKind.ShouldBe(MachineCredentialKind.ClientCredentials);
        stored.ObjectId.ShouldBe(created.ObjectId);
        stored.AccessKey.ShouldBe("sec***");

        // The project moves back to API keys; the existing M2M machine is still managed as one.
        await policyStore.SetAsync(Organization, Project, new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.ApiKey });

        var reset = await accessService.ResetMachineAccessAsync(Organization, Project, created.Id);
        reset.AccessKey.ShouldBe($"reset-secret-{created.ObjectId}");

        (await accessService.DeleteMachineAccessAsync(Organization, Project, created.Id)).ShouldBeTrue();
        (await accessService.VerifyAccessForMachineAsync(Organization, Project, created.Id)).ShouldBeFalse();

        identityProvider.Calls.ShouldContain($"create:{Organization}.{Project}");
        identityProvider.Calls.ShouldContain($"reset:{created.ObjectId}");
        identityProvider.Calls.ShouldContain($"delete:{created.ObjectId}");
    }
}
