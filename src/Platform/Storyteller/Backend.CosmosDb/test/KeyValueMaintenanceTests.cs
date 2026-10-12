#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.Audit;
using _42.Platform.Storyteller.KeyValues.Policy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Rotation of a key-encryption key, the re-wrap of the stored envelopes, and the change of a key's level.
public class KeyValueMaintenanceTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Org = TestConstants.Organization;
    private const string View = Constants.DefaultViewName;

    private static readonly KeyValueCaller Admin = KeyValueCaller.WithSecrets("account: admin");

    private IKeyValueService KeyValues => Context.Services.GetRequiredService<IKeyValueService>();

    private IKeyValuePolicyService Policies => Context.Services.GetRequiredService<IKeyValuePolicyService>();

    private IKeyValueMaintenanceService Maintenance => Context.Services.GetRequiredService<IKeyValueMaintenanceService>();

    private FakeKeyEncryptionProvider Standard => Context.Services.GetRequiredService<FakeKeyEncryptionProvider>();

    [Fact]
    public async Task Rotate_StartsARewrapThatMovesEveryEnvelopeAndKeepsTheTtl()
    {
        var project = await Context.CreateProjectWithAccessPointAsync("rotate");
        await KeyValues.SetAsync(Org, project, View, "a", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "a1" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "a", new KeyValueWrite { Value = "a2" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "b", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "b1" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "b", new KeyValueWrite { Value = "b2" }, Admin);
        await KeyValues.DeleteAsync(Org, project, View, "b", new KeyValueDeleteOptions(), Admin);
        await KeyValues.SetAsync(Org, project, "rollout", "c", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "c1" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "p", new KeyValueWrite { Protection = KeyValueProtection.Public, Value = "public" }, Admin);
        var versionBefore = Standard.CurrentVersion;
        var deletedBefore = await KeyValues.GetAsync(Org, project, View, "b", new KeyValueReadOptions { IncludeDeleted = true }, Admin);

        var rotated = await Policies.RotateAsync(Org, project, KeyValueProtection.Standard, Admin);

        rotated.Standard!.CurrentKeyVersion.Should().NotBe(versionBefore);
        rotated.Rewrap.Should().NotBeNull();
        rotated.Rewrap!.CompletedAt.Should().BeNull();
        rotated.Rewrap.TargetKeyVersion.Should().Be(Standard.CurrentVersion);

        var status = await Maintenance.RewrapAsync(Org, project, KeyValueProtection.Standard);

        status!.Processed.Should().Be(6, "a (catalog and 1 history item), b (deleted catalog and 2 history items) and c in the other view");
        status.CompletedAt.Should().NotBeNull();
        status.LastError.Should().BeNull();

        // every stored value still decrypts, under the new key version
        (await KeyValues.GetAsync(Org, project, View, "a", new KeyValueReadOptions { Reveal = true }, Admin))!.Value.Should().Be("a2");
        (await KeyValues.GetVersionAsync(Org, project, View, "a", 1, reveal: true, Admin))!.Value.Should().Be("a1");
        (await KeyValues.GetAsync(Org, project, "rollout", "c", new KeyValueReadOptions { Reveal = true }, Admin))!.Value.Should().Be("c1");
        var deletedAfter = await KeyValues.GetAsync(Org, project, View, "b", new KeyValueReadOptions { IncludeDeleted = true, Reveal = true }, Admin);
        deletedAfter!.Value.Should().Be("b2");
        deletedAfter.ExpiresAt.Should().BeCloseTo(deletedBefore!.ExpiresAt!.Value, TimeSpan.FromSeconds(5), "the re-wrap keeps the ttl");

        var again = await Maintenance.RewrapAsync(Org, project, KeyValueProtection.Standard);
        again!.Processed.Should().Be(0, "a run after the completed one is a new status with nothing to do");
        again.CompletedAt.Should().NotBeNull();

        var policy = await Context.Services.GetRequiredService<IKeyValuePolicyStore>().GetAsync(Org, project);
        policy.Rewrap!.CompletedAt.Should().NotBeNull();

        var events = await Context.Services.GetRequiredService<IKeyValueAuditLog>().QueryAsync(Org, project, new KeyValueAuditQuery());
        events.Items.Select(entry => entry.Operation).Should().Contain([KeyValueAuditOperation.Rotate, KeyValueAuditOperation.Rewrap]);
    }

    [Fact]
    public async Task DailyCheck_NoticesANewKeyVersion()
    {
        var project = await Context.CreateProjectWithAccessPointAsync("check");
        await KeyValues.SetAsync(Org, project, View, "s", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "v" }, Admin);

        var first = await Maintenance.CheckKeysAsync(Org, project, force: true);
        first.Standard.Should().NotBeNull("the free Standard key is made sure of");
        first.Rewrap.Should().BeNull();

        await Standard.RotateAsync(new KeyValues.Protection.KeyValueScope(Org, project), first);
        var second = await Maintenance.CheckKeysAsync(Org, project, force: true);

        second.Standard!.CurrentKeyVersion.Should().Be(Standard.CurrentVersion);
        second.Rewrap.Should().NotBeNull();
        second.Rewrap!.Protection.Should().Be(KeyValueProtection.Standard);

        await Maintenance.RunAsync();
        var afterRun = await Context.Services.GetRequiredService<IKeyValuePolicyStore>().GetAsync(Org, project);
        afterRun.Rewrap!.CompletedAt.Should().NotBeNull();
        (await KeyValues.GetAsync(Org, project, View, "s", new KeyValueReadOptions { Reveal = true }, Admin))!.Value.Should().Be("v");
    }

    [Fact]
    public async Task Protect_MovesAKeyBetweenTheServerSideLevels()
    {
        var project = await Context.CreateProjectWithAccessPointAsync("protect");
        await Policies.UpdateAsync(Org, project, new KeyValuePolicyUpdate
        {
            AllowedProtections = [KeyValueProtection.Public, KeyValueProtection.Standard, KeyValueProtection.Hsm],
        }, Admin);
        await KeyValues.SetAsync(Org, project, View, "s", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "v" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "p", new KeyValueWrite { Protection = KeyValueProtection.Public, Value = "x" }, Admin);

        var result = await KeyValues.ProtectAsync(Org, project, View, new KeyValueBulkRequest { KeyPrefix = string.Empty, Keys = ["s", "p"], Protection = KeyValueProtection.Hsm }, Admin);

        result.Items.Select(item => (item.Key, item.Outcome)).Should().Equal(("s", KeyValueBulkOutcome.Done), ("p", KeyValueBulkOutcome.NotAllowed));
        result.Items[0].Version.Should().Be(2);

        var current = await KeyValues.GetAsync(Org, project, View, "s", new KeyValueReadOptions { Reveal = true }, Admin);
        current!.Protection.Should().Be(KeyValueProtection.Hsm);
        current.Value.Should().Be("v");
        var versions = await KeyValues.GetVersionsAsync(Org, project, View, "s");
        versions.Select(version => version.Protection).Should().Equal(KeyValueProtection.Standard, KeyValueProtection.Hsm);
        (await KeyValues.GetVersionAsync(Org, project, View, "s", 1, reveal: true, Admin))!.Value.Should().Be("v", "the old version keeps its level and still decrypts");

        var byPrefix = await KeyValues.ProtectAsync(Org, project, View, new KeyValueBulkRequest { KeyPrefix = "s", Protection = KeyValueProtection.Standard }, Admin);
        byPrefix.Items.Single().Outcome.Should().Be(KeyValueBulkOutcome.Done);

        var notAllowed = () => KeyValues.ProtectAsync(Org, project, View, new KeyValueBulkRequest { Keys = ["s"], Protection = KeyValueProtection.Byok }, Admin);
        (await notAllowed.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.ProtectionNotAllowed);

        var reader = await KeyValues.ProtectAsync(Org, project, View, new KeyValueBulkRequest { Keys = ["s"], Protection = KeyValueProtection.Hsm }, KeyValueCaller.WithoutSecrets("account: reader"));
        reader.Items.Single().Outcome.Should().Be(KeyValueBulkOutcome.Forbidden);
    }
}
