using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Soft delete, restore, re-create and permanent delete of annotations, with their configurations and TTLs.
public class CosmosAnnotationLifecycleTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Project = "lifecycle";
    private const string OtherView = "second";
    private const int RetentionSeconds = 90 * 24 * 3600;

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    private IConfigurationService Configurations => Context.Services.GetRequiredService<IConfigurationService>();

    private Container Container => Context.Services
        .GetRequiredService<IContainerRepositoryProvider>()
        .GetOrganizationContainer(TestConstants.Organization)
        .Container;

    [Fact]
    public async Task Delete_Subject_HidesTheWholeTreeAndKeepsTheOtherAnnotations()
    {
        await CreateAsync("uxe.ds-s.ds-r.ds-c.ds-u");
        await CreateAsync("usg.ds-s.ds-r2");

        var result = await Annotations.DeleteAnnotationAsync(Key("sbt.ds-s"));

        result.DeletedKeys.Should().BeEquivalentTo(
            "uxe.ds-s.ds-r.ds-c.ds-u",
            "exe.ds-s.ds-r.ds-c",
            "usg.ds-s.ds-r",
            "usg.ds-s.ds-r2",
            "cnt.ds-s.ds-c",
            "sbt.ds-s");
        result.DeletedKeys[^1].Should().Be("sbt.ds-s");
        result.DeletedKeys[0].Should().Be("uxe.ds-s.ds-r.ds-c.ds-u");
        result.Deletion.IsRoot.Should().BeTrue();
        result.Deletion.DescendantCount.Should().Be(5);
        result.Deletion.PurgeAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(90), TimeSpan.FromMinutes(5));

        foreach (var key in result.DeletedKeys)
        {
            (await Annotations.GetAnnotationAsync(Key(key))).Should().BeNull();
            (await Annotations.ExistAnnotationAsync(Key(key))).Should().BeFalse();
        }

        var root = await Annotations.GetDeletedAnnotationAsync(Key("sbt.ds-s"));
        root!.Deletion!.IsRoot.Should().BeTrue();
        root.Deletion.DeletedBy.Id.Should().Be(AnnotationServiceTestExtensions.TestActor.Id);
        root.Deletion.PurgeAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(90), TimeSpan.FromMinutes(5));

        var execution = await Annotations.GetDeletedAnnotationAsync(Key("exe.ds-s.ds-r.ds-c"));
        execution!.Deletion!.IsRoot.Should().BeFalse();
        execution.Deletion.RootKey.Should().Be("sbt.ds-s");
        execution.Deletion.DeletionId.Should().Be(root.Deletion.DeletionId);

        (await Annotations.GetAnnotationAsync(Key("rst.ds-r"))).Should().NotBeNull();
        (await Annotations.GetAnnotationAsync(Key("unt.ds-r.ds-u"))).Should().NotBeNull();

        var listed = await ListAsync(AnnotationDeletedFilter.Exclude);
        listed.Should().NotContain(key => key.StartsWith("sbt.ds-s") || key.Contains(".ds-s."));

        var roots = await ListAsync(AnnotationDeletedFilter.Only, rootsOnly: true);
        roots.Should().Contain("sbt.ds-s");
        roots.Should().NotContain("exe.ds-s.ds-r.ds-c");
    }

    [Fact]
    public async Task Delete_Execution_DeletesItsUnitsOfExecutionAndLeavesTheArraysOfItsParents()
    {
        await CreateAsync("uxe.de-s.de-r.de-c.de-u");

        var result = await Annotations.DeleteAnnotationAsync(Key("exe.de-s.de-r.de-c"));

        result.DeletedKeys.Should().Equal("uxe.de-s.de-r.de-c.de-u", "exe.de-s.de-r.de-c");
        (await GetAsync<Usage>("usg.de-s.de-r")).ContextNames.Should().NotContain("de-c");
        (await GetAsync<Context>("cnt.de-s.de-c")).ResponsibilityNames.Should().NotContain("de-r");
        (await Annotations.GetAnnotationAsync(Key("unt.de-r.de-u"))).Should().NotBeNull();
    }

    [Fact]
    public async Task Delete_Unit_RemovesTheUnitFromItsResponsibilityAndExecutions()
    {
        await CreateAsync("uxe.du-s.du-r.du-c.du-u");

        var result = await Annotations.DeleteAnnotationAsync(Key("unt.du-r.du-u"));

        result.DeletedKeys.Should().Equal("uxe.du-s.du-r.du-c.du-u", "unt.du-r.du-u");
        (await GetAsync<Execution>("exe.du-s.du-r.du-c")).UnitNames.Should().NotContain("du-u");
        (await GetAsync<Responsibility>("rst.du-r")).UnitNames.Should().NotContain("du-u");
    }

    [Fact]
    public async Task Delete_Responsibility_LeavesTheSameResponsibilityInOtherViews()
    {
        await CreateAsync("usg.dr-s.dr-r");
        await CreateAsync("usg.dr-s.dr-r", OtherView);

        await Annotations.DeleteAnnotationAsync(Key("rst.dr-r"));

        (await Annotations.GetAnnotationAsync(Key("rst.dr-r", OtherView))).Should().NotBeNull();
        (await Annotations.GetAnnotationAsync(Key("usg.dr-s.dr-r", OtherView))).Should().NotBeNull();
        (await Annotations.GetAnnotationAsync(Key("usg.dr-s.dr-r"))).Should().BeNull();
        (await GetAsync<Subject>("sbt.dr-s")).ResponsibilityNames.Should().NotContain("dr-r");
    }

    [Fact]
    public async Task Delete_Twice_ReturnsTheExistingDeletion()
    {
        await CreateAsync("sbt.dt-s");

        var first = await Annotations.DeleteAnnotationAsync(Key("sbt.dt-s"));
        var second = await Annotations.DeleteAnnotationAsync(Key("sbt.dt-s"));

        second.DeletedKeys.Should().BeEmpty();
        second.Deletion.DeletionId.Should().Be(first.Deletion.DeletionId);
    }

    [Fact]
    public async Task Delete_SetsTtlAndRestore_RemovesIt()
    {
        await CreateAsync("sbt.tt-s");
        await Configurations.CreateOrUpdateConfigurationAsync(Key("sbt.tt-s"), JObject.Parse("""{ "a": 1 }"""), "tester");

        await Annotations.DeleteAnnotationAsync(Key("sbt.tt-s"));

        var partitionKey = $"{Project}.sbt.tt-s";
        var annotation = await ReadRawAsync("default.sbt.tt-s", partitionKey);
        var configuration = await ReadRawAsync("default.cnf.sbt.tt-s", partitionKey);
        var audit = await ReadRawAsync("default.adt.sbt.tt-s", partitionKey);

        annotation!.Value.GetProperty("ttl").GetInt32().Should().BeInRange(RetentionSeconds - 600, RetentionSeconds);
        configuration!.Value.GetProperty("ttl").GetInt32().Should().BeInRange(RetentionSeconds - 600, RetentionSeconds);
        configuration.Value.TryGetProperty("AnnotationDeletionId", out _).Should().BeTrue();
        audit!.Value.GetProperty("ttl").GetInt32().Should().BeInRange(RetentionSeconds - 600, RetentionSeconds);
        (await ReadRawAsync("default.cfv.sbt.tt-s.1", partitionKey)).Should().NotBeNull("the deletion keeps the last content as a version");

        await Annotations.RestoreAnnotationsAsync(TestConstants.Organization, Project, Constants.DefaultViewName, [AnnotationKey.Parse("sbt.tt-s")], AnnotationServiceTestExtensions.TestActor);

        annotation = await ReadRawAsync("default.sbt.tt-s", partitionKey);
        configuration = await ReadRawAsync("default.cnf.sbt.tt-s", partitionKey);
        audit = await ReadRawAsync("default.adt.sbt.tt-s", partitionKey);

        annotation!.Value.TryGetProperty("ttl", out _).Should().BeFalse();
        configuration!.Value.TryGetProperty("ttl", out _).Should().BeFalse();
        configuration.Value.TryGetProperty("AnnotationDeletionId", out _).Should().BeFalse();
        audit!.Value.TryGetProperty("ttl", out _).Should().BeFalse();
        (await ReadRawAsync("default.cfv.sbt.tt-s.1", partitionKey)).Should().BeNull("the restored configuration holds the version again");
        (await Configurations.GetConfigurationVersionsAsync(Key("sbt.tt-s"))).Should().ContainSingle();
    }

    [Fact]
    public async Task Restore_WithDescendants_BringsBackTheTreeAndItsConfiguration()
    {
        await CreateAsync("exe.rd-s.rd-r.rd-c");
        await Configurations.CreateOrUpdateConfigurationAsync(Key("exe.rd-s.rd-r.rd-c"), JObject.Parse("""{ "x": 1 }"""), "tester");
        await Annotations.DeleteAnnotationAsync(Key("sbt.rd-s"));

        var result = await RestoreAsync(includeDescendants: true, "sbt.rd-s");

        result.Failed.Should().BeEmpty();
        result.Annotations.Select(a => a.AnnotationKey).Should().BeEquivalentTo("sbt.rd-s", "usg.rd-s.rd-r", "cnt.rd-s.rd-c", "exe.rd-s.rd-r.rd-c");
        result.Annotations[0].AnnotationKey.Should().Be("sbt.rd-s");

        var subject = await GetAsync<Subject>("sbt.rd-s");
        subject.ResponsibilityNames.Should().Contain("rd-r");
        subject.ContextNames.Should().Contain("rd-c");

        var configuration = await Configurations.GetRawConfigurationAsync(Key("exe.rd-s.rd-r.rd-c"));
        configuration!.Content.Value<int>("x").Should().Be(1);

        var history = await Annotations.GetAnnotationHistoryAsync(Key("sbt.rd-s"));
        history!.Entries.Select(e => e.Action).Should().Equal(AnnotationAuditAction.Restored, AnnotationAuditAction.Deleted, AnnotationAuditAction.Created);
        history.Entries[0].DescendantCount.Should().Be(3);

        var executionHistory = await Annotations.GetAnnotationHistoryAsync(Key("exe.rd-s.rd-r.rd-c"));
        executionHistory!.Entries[0].Cause!.Kind.Should().Be(AnnotationAuditCauseKind.Cascade);
        executionHistory.Entries[0].Cause!.Key.Should().Be("sbt.rd-s");
    }

    [Fact]
    public async Task Restore_Descendant_RestoresItsAncestorsAloneAndKeepsTheOthersDeleted()
    {
        await CreateAsync("exe.ra-s.ra-r.ra-c");
        await CreateAsync("usg.ra-s.ra-r2");
        await Annotations.DeleteAnnotationAsync(Key("sbt.ra-s"));

        var result = await RestoreAsync(includeDescendants: true, "exe.ra-s.ra-r.ra-c");

        result.Annotations.Select(a => a.AnnotationKey).Should().Equal("sbt.ra-s", "usg.ra-s.ra-r", "cnt.ra-s.ra-c", "exe.ra-s.ra-r.ra-c");

        var subject = await GetAsync<Subject>("sbt.ra-s");
        subject.ResponsibilityNames.Should().BeEquivalentTo("ra-r");
        subject.ContextNames.Should().BeEquivalentTo("ra-c");

        var other = await Annotations.GetDeletedAnnotationAsync(Key("usg.ra-s.ra-r2"));
        other!.Deletion!.IsRoot.Should().BeTrue("its parent is active again");

        var subjectHistory = await Annotations.GetAnnotationHistoryAsync(Key("sbt.ra-s"));
        subjectHistory!.Entries[0].Cause!.Kind.Should().Be(AnnotationAuditCauseKind.Ancestor);
        subjectHistory.Entries[0].Cause!.Key.Should().Be("exe.ra-s.ra-r.ra-c");
    }

    [Fact]
    public async Task Restore_WithoutDescendants_KeepsTheChildrenDeletedAsRoots()
    {
        await CreateAsync("usg.rw-s.rw-r");
        await CreateAsync("cnt.rw-s.rw-c");
        await Annotations.DeleteAnnotationAsync(Key("sbt.rw-s"));

        var result = await RestoreAsync(includeDescendants: false, "sbt.rw-s");

        result.Annotations.Select(a => a.AnnotationKey).Should().Equal("sbt.rw-s");

        var subject = await GetAsync<Subject>("sbt.rw-s");
        subject.ResponsibilityNames.Should().BeEmpty();
        subject.ContextNames.Should().BeEmpty();

        (await Annotations.GetDeletedAnnotationAsync(Key("usg.rw-s.rw-r")))!.Deletion!.IsRoot.Should().BeTrue();
        (await Annotations.GetDeletedAnnotationAsync(Key("cnt.rw-s.rw-c")))!.Deletion!.IsRoot.Should().BeTrue();
    }

    [Fact]
    public async Task Restore_Usage_PutsItsExecutionsBackIntoTheirContexts()
    {
        await CreateAsync("exe.rc-s2.rc-r.rc-c");
        await Annotations.DeleteAnnotationAsync(Key("usg.rc-s2.rc-r"));

        (await GetAsync<Context>("cnt.rc-s2.rc-c")).ResponsibilityNames.Should().NotContain("rc-r");
        (await GetAsync<Subject>("sbt.rc-s2")).ResponsibilityNames.Should().NotContain("rc-r");

        await RestoreAsync(includeDescendants: true, "usg.rc-s2.rc-r");

        (await GetAsync<Context>("cnt.rc-s2.rc-c")).ResponsibilityNames.Should().Contain("rc-r");
        (await GetAsync<Subject>("sbt.rc-s2")).ResponsibilityNames.Should().Contain("rc-r");
        (await GetAsync<Usage>("usg.rc-s2.rc-r")).ContextNames.Should().Contain("rc-c");
        (await Annotations.GetAnnotationAsync(Key("exe.rc-s2.rc-r.rc-c"))).Should().NotBeNull();
    }

    [Fact]
    public async Task Restore_UnknownKey_IsReportedAsAFailure()
    {
        await CreateAsync("sbt.ru-s");
        await Annotations.DeleteAnnotationAsync(Key("sbt.ru-s"));

        var result = await RestoreAsync(includeDescendants: true, "sbt.ru-s", "sbt.ru-never");

        result.Annotations.Select(a => a.AnnotationKey).Should().Equal("sbt.ru-s");
        result.Failed.Should().ContainSingle(f => f.Key == "sbt.ru-never" && f.ErrorCode == ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Restore_ExpiredAncestor_IsCreatedAgain()
    {
        await CreateAsync("usg.rx-s.rx-r");
        await Annotations.DeleteAnnotationAsync(Key("sbt.rx-s"));

        // the subject expired before its usage (as if its ttl had run out first)
        using (await Container.DeleteItemStreamAsync("default.sbt.rx-s", new PartitionKey($"{Project}.sbt.rx-s")))
        {
        }

        var result = await RestoreAsync(includeDescendants: true, "usg.rx-s.rx-r");

        result.CreatedKeys.Should().Equal("sbt.rx-s");
        (await GetAsync<Subject>("sbt.rx-s")).ResponsibilityNames.Should().Contain("rx-r");
    }

    [Fact]
    public async Task Create_DeletedKey_StartsANewLifetimeWithoutConfiguration()
    {
        await CreateAsync("usg.rc-s.rc-r");
        await Configurations.CreateOrUpdateConfigurationAsync(Key("sbt.rc-s"), JObject.Parse("""{ "a": 1 }"""), "tester");
        await Configurations.CreateOrUpdateConfigurationAsync(Key("sbt.rc-s"), JObject.Parse("""{ "a": 2 }"""), "tester");
        await Annotations.DeleteAnnotationAsync(Key("sbt.rc-s"));

        await Annotations.CreateAnnotationAsync(TestConstants.Organization, Model("sbt.rc-s") with { Title = "New lifetime" });

        var subject = await GetAsync<Subject>("sbt.rc-s");
        subject.Title.Should().Be("New lifetime");
        subject.ResponsibilityNames.Should().BeEmpty();
        subject.Deletion.Should().BeNull();
        (await Configurations.HasConfigurationContentAsync(Key("sbt.rc-s"))).Should().BeFalse();

        var usage = await Annotations.GetDeletedAnnotationAsync(Key("usg.rc-s.rc-r"));
        usage!.Deletion!.IsRoot.Should().BeTrue();

        var history = await Annotations.GetAnnotationHistoryAsync(Key("sbt.rc-s"));
        history!.Entries.Select(e => e.Action).Should().Equal(AnnotationAuditAction.Recreated, AnnotationAuditAction.Deleted, AnnotationAuditAction.Created);
        history.Entries[0].DeletionId.Should().Be(history.Entries[1].DeletionId);

        var next = await Configurations.CreateOrUpdateConfigurationAsync(Key("sbt.rc-s"), JObject.Parse("""{ "b": 3 }"""), "tester");
        next.Version.Should().Be(3, "the versions of the previous lifetime stay");
        (await Configurations.GetConfigurationVersionsAsync(Key("sbt.rc-s"))).Select(v => v.Version).Should().Equal(1u, 2u, 3u);
    }

    [Fact]
    public async Task Create_UnderDeletedAncestor_RecreatesTheAncestorEmpty()
    {
        await Annotations.CreateAnnotationAsync(TestConstants.Organization, Model("sbt.ca-s") with { Title = "Subject" });
        await CreateAsync("cnt.ca-s.ca-c");
        await Annotations.DeleteAnnotationAsync(Key("sbt.ca-s"));

        var created = (await Annotations.CreateAnnotationAsync(TestConstants.Organization, Model("usg.ca-s.ca-r"))).ToList();

        created.Select(a => a.AnnotationKey).Should().Equal("rst.ca-r", "sbt.ca-s", "usg.ca-s.ca-r");

        var subject = await GetAsync<Subject>("sbt.ca-s");
        subject.Title.Should().BeNull();
        subject.ResponsibilityNames.Should().BeEquivalentTo("ca-r");
        subject.ContextNames.Should().BeEmpty();

        var history = await Annotations.GetAnnotationHistoryAsync(Key("sbt.ca-s"));
        history!.Entries[0].Action.Should().Be(AnnotationAuditAction.Recreated);
        history.Entries[0].Cause!.Kind.Should().Be(AnnotationAuditCauseKind.Ancestor);
        history.Entries[0].Cause!.Key.Should().Be("usg.ca-s.ca-r");

        (await Annotations.GetDeletedAnnotationAsync(Key("cnt.ca-s.ca-c")))!.Deletion!.IsRoot.Should().BeTrue();
    }

    [Fact]
    public async Task Purge_RemovesTheTreeAndItsAuditsButKeepsTheVersions()
    {
        await CreateAsync("exe.pg-s.pg-r.pg-c");
        await Configurations.CreateOrUpdateConfigurationAsync(Key("exe.pg-s.pg-r.pg-c"), JObject.Parse("""{ "p": 1 }"""), "tester");

        var active = async () => await Annotations.PurgeAnnotationAsync(Key("sbt.pg-s"), AnnotationServiceTestExtensions.TestActor);
        (await active.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.AnnotationNotDeleted);

        await Annotations.DeleteAnnotationAsync(Key("sbt.pg-s"));
        var result = await Annotations.PurgeAnnotationAsync(Key("sbt.pg-s"), AnnotationServiceTestExtensions.TestActor);

        result.PurgedKeys.Should().BeEquivalentTo("sbt.pg-s", "usg.pg-s.pg-r", "cnt.pg-s.pg-c", "exe.pg-s.pg-r.pg-c");
        result.PurgedKeys[^1].Should().Be("sbt.pg-s");

        (await Annotations.GetDeletedAnnotationAsync(Key("sbt.pg-s"))).Should().BeNull();
        (await Annotations.GetAnnotationHistoryAsync(Key("exe.pg-s.pg-r.pg-c"))).Should().BeNull();
        (await ReadRawAsync("default.adt.exe.pg-s.pg-r.pg-c", $"{Project}.rst.pg-r")).Should().BeNull();
        (await Annotations.GetAnnotationAsync(Key("rst.pg-r"))).Should().NotBeNull();
        (await Configurations.GetConfigurationVersionsAsync(Key("exe.pg-s.pg-r.pg-c"))).Select(v => v.Version).Should().Equal(1u);
    }

    [Fact]
    public async Task FindDeleted_ReturnsOnlyTheDeletedKeys()
    {
        await CreateAsync("sbt.fd-a");
        await CreateAsync("sbt.fd-b");
        await Annotations.DeleteAnnotationAsync(Key("sbt.fd-a"));

        var found = await Annotations.FindDeletedAnnotationsAsync(
            TestConstants.Organization,
            Project,
            Constants.DefaultViewName,
            [AnnotationKey.Parse("sbt.fd-a"), AnnotationKey.Parse("sbt.fd-b"), AnnotationKey.Parse("sbt.fd-missing")]);

        found.Select(a => a.AnnotationKey).Should().Equal("sbt.fd-a");
        found[0].Deletion.Should().NotBeNull();
    }

    [Fact]
    public async Task DeletedAnnotation_HidesItsConfigurationAndRejectsChanges()
    {
        await CreateAsync("sbt.cf-s");
        await Configurations.CreateOrUpdateConfigurationAsync(Key("sbt.cf-s"), JObject.Parse("""{ "c": 1 }"""), "tester");
        await Annotations.DeleteAnnotationAsync(Key("sbt.cf-s"));

        (await Configurations.GetRawConfigurationAsync(Key("sbt.cf-s"))).Should().BeNull();
        (await Configurations.HasConfigurationContentAsync(Key("sbt.cf-s"))).Should().BeFalse();
        (await Configurations.GetConfigurationVersionsAsync(Key("sbt.cf-s"))).Should().BeEmpty();

        var listing = await Configurations.ListConfigurationsAsync(TestConstants.Organization, Project, Constants.DefaultViewName, keyPrefix: "sbt.cf-s");
        listing.Configurations.Should().BeEmpty();

        var set = async () => await Configurations.CreateOrUpdateConfigurationAsync(Key("sbt.cf-s"), JObject.Parse("""{ "c": 2 }"""), "tester");
        (await set.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.AnnotationDeleted);

        var patch = async () => await Configurations.PatchConfigurationAsync(Key("sbt.cf-s"), JArray.Parse("""[{ "op": "replace", "path": "/c", "value": 3 }]"""), "tester");
        (await patch.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.AnnotationDeleted);

        var delete = async () => await Configurations.DeleteAsync(Key("sbt.cf-s"));
        (await delete.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.AnnotationDeleted);
    }

    [Fact]
    public async Task Create_InvalidTitle_IsRejected()
    {
        var create = async () => await Annotations.CreateAnnotationAsync(TestConstants.Organization, Model("sbt.iv-s") with { Title = new string('t', 201) });

        (await create.Should().ThrowAsync<InvalidInputException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidAnnotation);
        (await Annotations.GetAnnotationAsync(Key("sbt.iv-s"))).Should().BeNull();
    }

    [Fact]
    public async Task OrganizationContainer_HasTtlAndKeepsAuditEntriesOutOfTheIndex()
    {
        var properties = (await Container.ReadContainerAsync()).Resource;

        properties.DefaultTimeToLive.Should().Be(-1);
        properties.IndexingPolicy.ExcludedPaths.Should().Contain(path => path.Path == ContainerFactory.AuditEntriesPath);
    }

    private FullKey Key(string key, string view = Constants.DefaultViewName)
    {
        return FullKey.Create(AnnotationKey.Parse(key), TestConstants.Organization, Project, view);
    }

    private static Annotation Model(string key, string view = Constants.DefaultViewName)
    {
        return AnnotationFactory.CreateDefault(AnnotationKey.Parse(key), Project, view);
    }

    private Task CreateAsync(string key, string view = Constants.DefaultViewName)
    {
        return Annotations.CreateAnnotationAsync(TestConstants.Organization, Model(key, view));
    }

    private async Task<TAnnotation> GetAsync<TAnnotation>(string key)
        where TAnnotation : Annotation
    {
        var annotation = await Annotations.GetAnnotationAsync(Key(key));
        annotation.Should().NotBeNull($"'{key}' should be active");
        return (TAnnotation)annotation!;
    }

    private Task<AnnotationRestoreResult> RestoreAsync(bool includeDescendants, params string[] keys)
    {
        return Annotations.RestoreAnnotationsAsync(
            TestConstants.Organization,
            Project,
            Constants.DefaultViewName,
            keys.Select(AnnotationKey.Parse).ToList(),
            AnnotationServiceTestExtensions.TestActor,
            includeDescendants);
    }

    private async Task<List<string>> ListAsync(AnnotationDeletedFilter filter, bool rootsOnly = false)
    {
        var keys = new List<string>();
        string token = null;

        do
        {
            var page = await Annotations.GetAnnotationsAsync(new AnnotationsRequest
            {
                Organization = TestConstants.Organization,
                Project = Project,
                View = Constants.DefaultViewName,
                Types = Enum.GetValues<AnnotationType>(),
                Deleted = filter,
                DeletionRootsOnly = rootsOnly,
                ContinuationToken = token,
            });

            keys.AddRange(page.Annotations.Select(a => a.AnnotationKey));
            token = page.ContinuationToken;
        }
        while (token is not null);

        return keys;
    }

    private async Task<JsonElement?> ReadRawAsync(string id, string partitionKey)
    {
        using var response = await Container.ReadItemStreamAsync(id, new PartitionKey(partitionKey));

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        using var document = await JsonDocument.ParseAsync(response.Content);
        return document.RootElement.Clone();
    }
}
