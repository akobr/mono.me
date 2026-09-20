using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace _42.Platform.Storyteller.Access.Certificates.IntegrationTests;

/// <summary>
/// Tests per-project machine authentication policy storage and enforcement.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class PolicyEnforcementTests(CosmosFixture fixture)
{
    private const string Organization = "testorg";
    private const string Project = "policyproj";

    [Fact]
    public async Task GetPolicy_NoneSet_ReturnsNull()
    {
        var policyStore = fixture.Services.GetRequiredService<IMachineAuthenticationPolicyStore>();

        var policy = await policyStore.GetAsync(Organization, "nonexistent-project");

        policy.ShouldBeNull();
    }

    [Fact]
    public async Task SetAndGetPolicy_Roundtrips()
    {
        var policyStore = fixture.Services.GetRequiredService<IMachineAuthenticationPolicyStore>();
        await EnsureAccessPointAsync();

        var policy = new MachineAuthenticationPolicy
        {
            CredentialKind = MachineCredentialKind.CertificateAndApiKey,
            CertificateLifetimeDays = 180,
        };

        await policyStore.SetAsync(Organization, Project, policy);
        var retrieved = await policyStore.GetAsync(Organization, Project);

        retrieved.ShouldNotBeNull();
        retrieved.CredentialKind.ShouldBe(MachineCredentialKind.CertificateAndApiKey);
        retrieved.CertificateLifetimeDays.ShouldBe(180);
    }

    [Fact]
    public async Task SetPolicy_UpdatesExisting()
    {
        var policyStore = fixture.Services.GetRequiredService<IMachineAuthenticationPolicyStore>();
        await EnsureAccessPointAsync();

        await policyStore.SetAsync(Organization, Project, new MachineAuthenticationPolicy
        {
            CredentialKind = MachineCredentialKind.ApiKey,
        });

        await policyStore.SetAsync(Organization, Project, new MachineAuthenticationPolicy
        {
            CredentialKind = MachineCredentialKind.Certificate,
            CertificateLifetimeDays = 90,
        });

        var retrieved = await policyStore.GetAsync(Organization, Project);

        retrieved.ShouldNotBeNull();
        retrieved.CredentialKind.ShouldBe(MachineCredentialKind.Certificate);
        retrieved.CertificateLifetimeDays.ShouldBe(90);
    }

    private async Task EnsureAccessPointAsync()
    {
        try
        {
            var accessService = fixture.Services.GetRequiredService<IAccessService>();
            await accessService.CreateAccessPointAsync(new AccessPointCreate
            {
                Organization = Organization,
                Project = Project,
                OwnerId = "test-owner",
            });
        }
        catch
        {
            // Already exists.
        }
    }
}
