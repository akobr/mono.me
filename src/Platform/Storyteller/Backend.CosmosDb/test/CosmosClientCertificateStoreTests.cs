#nullable enable

using System;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Machine certificates in Cosmos: another instance (another cache) sees what one instance stored and revoked.
public class CosmosClientCertificateStoreTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Org = TestConstants.Organization;

    private CosmosClientCertificateStore CreateStore()
    {
        return new CosmosClientCertificateStore(
            Context.Services.GetRequiredService<IContainerRepositoryProvider>(),
            Context.Services.GetRequiredService<IOptions<JsonSerializerSettings>>(),
            Context.Services.GetRequiredService<IOptions<System.Text.Json.JsonSerializerOptions>>(),
            new MemoryCache(new MemoryCacheOptions()));
    }

    private static CertificateRecord NewRecord(string machineId, string thumbprint, string? previous = null) => new(
        machineId,
        thumbprint,
        previous,
        previous is null ? null : DateTimeOffset.UtcNow.AddDays(7),
        Guid.NewGuid().ToString("N"),
        DateTimeOffset.UtcNow.AddMinutes(-1),
        DateTimeOffset.UtcNow.AddDays(90),
        false,
        MachineAccessScope.DefaultRead,
        "exe.a.b.c",
        previous is null ? null : DateTimeOffset.UtcNow);

    [Fact]
    public async Task Store_IsSeenByAnotherInstance_AndRevocationFollows()
    {
        var project = $"certs-{Guid.NewGuid():N}"[..20];
        var issuing = CreateStore();
        var validating = CreateStore();
        var machine = Guid.NewGuid().ToString("D");
        var thumbprint = Guid.NewGuid().ToString("N").ToUpperInvariant();

        (await validating.GetByThumbprintAsync(Org, project, thumbprint)).Should().BeNull();
        await issuing.StoreAsync(Org, project, NewRecord(machine, thumbprint));

        // the miss was cached for 30 seconds on the validating instance; a fresh instance sees the record at once
        var fresh = CreateStore();
        var byThumbprint = await fresh.GetByThumbprintAsync(Org, project, thumbprint.ToLowerInvariant());
        byThumbprint.Should().NotBeNull();
        byThumbprint!.MachineAccessId.Should().Be(machine);
        byThumbprint.Scope.Should().Be(MachineAccessScope.DefaultRead);
        byThumbprint.AnnotationKey.Should().Be("exe.a.b.c");
        (await fresh.GetByMachineAccessIdAsync(Org, project, machine))!.Thumbprint.Should().Be(thumbprint);

        // a renewal stores a second record; the machine resolves to the newest one
        var renewed = Guid.NewGuid().ToString("N").ToUpperInvariant();
        await issuing.StoreAsync(Org, project, NewRecord(machine, renewed, previous: thumbprint) with { NotAfter = DateTimeOffset.UtcNow.AddDays(180) });
        (await CreateStore().GetByMachineAccessIdAsync(Org, project, machine))!.Thumbprint.Should().Be(renewed);
        (await CreateStore().GetByThumbprintAsync(Org, project, thumbprint)).Should().NotBeNull("the previous certificate is still known");

        await issuing.RevokeAsync(Org, project, machine);
        var revoked = await CreateStore().GetByMachineAccessIdAsync(Org, project, machine);
        revoked!.IsRevoked.Should().BeTrue();
        (await CreateStore().GetByThumbprintAsync(Org, project, thumbprint))!.IsRevoked.Should().BeTrue("every certificate of the machine is revoked");

        (await CreateStore().GetByMachineAccessIdAsync(Org, "other-project", machine)).Should().BeNull("projects are isolated");
    }

    [Fact]
    public async Task CredentialKind_IsStored_AndAnOlderRecordTakesItFromTheMachine()
    {
        var project = $"certk-{Guid.NewGuid():N}"[..20];
        var store = CreateStore();
        var machine = Guid.NewGuid().ToString("D");
        var thumbprint = Guid.NewGuid().ToString("N").ToUpperInvariant();

        await store.StoreAsync(Org, project, NewRecord(machine, thumbprint) with { CredentialKind = MachineCredentialKind.CertificateAndApiKey });
        (await CreateStore().GetByThumbprintAsync(Org, project, thumbprint))!.CredentialKind.Should().Be(MachineCredentialKind.CertificateAndApiKey);

        // a record without the kind, as stored before it was kept: the kind comes from the machine access document
        var older = Guid.NewGuid().ToString("D");
        var olderThumbprint = Guid.NewGuid().ToString("N").ToUpperInvariant();
        await store.StoreAsync(Org, project, NewRecord(older, olderThumbprint));
        (await CreateStore().GetByMachineAccessIdAsync(Org, project, older))!.CredentialKind.Should().BeNull("the machine has no document");

        var container = Context.Services.GetRequiredService<IContainerRepositoryProvider>().GetOrganizationContainer(Org).Container;
        await container.UpsertItemAsync(
            new Entities.Access.MachineAccessEntity
            {
                PartitionKey = CosmosClientCertificateStore.GetPartitionKey(project),
                Id = older,
                ObjectId = older,
                AccessKey = "***",
                Scope = MachineAccessScope.DefaultRead,
                CredentialKind = MachineCredentialKind.CertificateAndApiKey,
            },
            new Microsoft.Azure.Cosmos.PartitionKey(CosmosClientCertificateStore.GetPartitionKey(project)));

        (await CreateStore().GetByMachineAccessIdAsync(Org, project, older))!.CredentialKind.Should().Be(MachineCredentialKind.CertificateAndApiKey);
        (await CreateStore().GetByThumbprintAsync(Org, project, olderThumbprint))!.CredentialKind.Should().Be(MachineCredentialKind.CertificateAndApiKey);
    }

    [Fact]
    public void Registration_ReplacesTheInMemoryStore()
    {
        Context.Services.GetRequiredService<IClientCertificateStore>().Should().BeOfType<CosmosClientCertificateStore>();
    }
}
