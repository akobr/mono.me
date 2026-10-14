#nullable enable

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The shared certificate list is cached per project; a write evicts it on the writing instance, others follow on expiry.
public class CosmosSharedCertificateStoreTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Org = TestConstants.Organization;

    private CosmosSharedCertificateStore CreateStore()
    {
        return new CosmosSharedCertificateStore(
            Context.Services.GetRequiredService<IContainerRepositoryProvider>(),
            Context.Services.GetRequiredService<IOptions<JsonSerializerOptions>>(),
            new MemoryCache(new MemoryCacheOptions()));
    }

    private static SharedCertificate NewCertificate(string label) => new()
    {
        Thumbprint = Guid.NewGuid().ToString("N").ToUpperInvariant(),
        Label = label,
        NotBefore = DateTimeOffset.UtcNow.AddMinutes(-1),
        NotAfter = DateTimeOffset.UtcNow.AddDays(90),
    };

    [Fact]
    public async Task List_IsCachedAndEvictedByWritesOnTheSameInstance()
    {
        var project = $"scert-{Guid.NewGuid():N}"[..20];
        var store = CreateStore();
        var other = CreateStore();

        (await store.ListAsync(Org, project)).Should().BeEmpty();

        var first = NewCertificate("ci");
        await store.StoreAsync(Org, project, first);
        (await store.ListAsync(Org, project)).Should().ContainSingle(c => c.Thumbprint == first.Thumbprint, "the write evicted the cached empty list");

        (await other.ListAsync(Org, project)).Should().ContainSingle();

        await store.RevokeAsync(Org, project, first.Thumbprint);
        (await store.ListAsync(Org, project)).Single().IsRevoked.Should().BeTrue("the revocation evicted the cached list");
        (await other.ListAsync(Org, project)).Single().IsRevoked.Should().BeFalse("another instance keeps its cached list until it expires");
        (await CreateStore().ListAsync(Org, project)).Single().IsRevoked.Should().BeTrue();
    }

    [Fact]
    public void Registration_UsesTheCosmosStore()
    {
        Context.Services.GetRequiredService<ISharedCertificateStore>().Should().BeOfType<CosmosSharedCertificateStore>();
    }
}
