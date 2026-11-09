#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Tenant isolation of annotations in the shared container "organizations": two organizations with the same project,
// view and annotation keys, told apart only by Organization, the first level of the partition key. Every read, list,
// descendant query, view listing and history through one organization answers its own items, and every write,
// delete, restore and purge through it leaves the other one as it was. The titles name the organization ("a:…",
// "b:…"), so a leaked item shows even under an identical key; B also has a second responsibility under the same
// subject, so a leak of B shows by key as well. Every test works in two organizations of its own.
public class AnnotationIsolationTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Project = "billing";
    private const string DefaultView = Constants.DefaultViewName;
    private const string ProdView = "prod";
    private const string MarkerA = "a";
    private const string MarkerB = "b";
    private const string SubjectKey = "sbt.iso-s";
    private const int SmallPageSize = 2;

    // far more pages than any list here needs: a list that does not end fails instead of looping
    private const int MaxPages = 500;

    // one annotation of every type, ancestors first, so each create writes its own key only
    private static readonly string[] Tree =
    [
        "rst.iso-r",
        "sbt.iso-s",
        "unt.iso-r.iso-u",
        "usg.iso-s.iso-r",
        "cnt.iso-s.iso-c",
        "exe.iso-s.iso-r.iso-c",
        "uxe.iso-s.iso-r.iso-c.iso-u",
    ];

    // only in B: a second responsibility and its usage of the shared subject, a partition A does not have
    private static readonly string[] OnlyInB = ["rst.iso-rb", "usg.iso-s.iso-rb"];

    // what a delete of the subject removes in A: its context in the subject's partition, and its usage, execution and
    // unit of execution in the partition of the responsibility (a query across the partitions of the project)
    private static readonly string[] SubjectTree =
    [
        "sbt.iso-s",
        "cnt.iso-s.iso-c",
        "usg.iso-s.iso-r",
        "exe.iso-s.iso-r.iso-c",
        "uxe.iso-s.iso-r.iso-c.iso-u",
    ];

    // the keys the writes test adds to A
    private static readonly string[] CreatedInA = ["unt.iso-r.iso-u2", "uxe.iso-s.iso-r.iso-c.iso-u2"];

    private static readonly string[] TreeOfB = [.. Tree, .. OnlyInB];

    private static readonly string[] SubjectTreeOfB = [.. SubjectTree, "usg.iso-s.iso-rb"];

    // every key a test reads or writes in either organization: the snapshots read them all
    private static readonly string[] KnownKeys = [.. Tree, .. OnlyInB, .. CreatedInA];

    private static readonly AuditActor ActorA = AuditActor.Account("iso-user-a", "Isolation A");

    private static readonly AuditActor ActorB = AuditActor.Account("iso-user-b", "Isolation B");

    // xUnit creates an instance per test, so every test has two organizations of its own, also on a reused emulator
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..12];

    private string OrgA => $"iso-ann-a-{_suffix}";

    private string OrgB => $"iso-ann-b-{_suffix}";

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    private IViewService Views => Context.Services.GetRequiredService<IViewService>();

    // a service of its own with a small page size, so the lists page across the types and continuation tokens
    private CosmosAnnotationService Paged => new(
        Context.Services.GetRequiredService<IContainerRepositoryProvider>(),
        Context.Services.GetRequiredService<AnnotationItemSerializer>(),
        Context.Services.GetRequiredService<AnnotationAuditStore>(),
        Context.Services.GetRequiredService<IOptions<AnnotationOptions>>(),
        Context.Services.GetRequiredService<TimeProvider>())
    {
        PageSize = SmallPageSize,
    };

    [Fact]
    public async Task PointReads_OfOneOrganization_ReadItsOwnItems()
    {
        await SeedAsync();

        foreach (var key in Tree)
        {
            (await Annotations.GetAnnotationAsync(Key(OrgA, key)))!.Title.Should().Be(Title(MarkerA, key));
            (await Annotations.GetAnnotationAsync(Key(OrgB, key)))!.Title.Should().Be(Title(MarkerB, key));

            // the audit items have the same ids in both organizations
            (await Annotations.GetAnnotationHistoryAsync(Key(OrgA, key)))!.Entries.Should().OnlyContain(entry => entry.Actor.Id == ActorA.Id);
            (await Annotations.GetAnnotationHistoryAsync(Key(OrgB, key)))!.Entries.Should().OnlyContain(entry => entry.Actor.Id == ActorB.Id);
        }

        foreach (var key in OnlyInB)
        {
            (await Annotations.ExistAnnotationAsync(Key(OrgA, key))).Should().BeFalse();
            (await Annotations.GetAnnotationAsync(Key(OrgA, key))).Should().BeNull();
            (await Annotations.GetAnnotationHistoryAsync(Key(OrgA, key))).Should().BeNull();
            (await Annotations.ExistAnnotationAsync(Key(OrgB, key))).Should().BeTrue();
        }

        // one read of many items, by the full keys of the organization
        var keys = KnownKeys.Select(AnnotationKey.Parse).ToList();
        (await Annotations.FindActiveAnnotationKeysAsync(OrgA, Project, DefaultView, keys, TestContext.Current.CancellationToken))
            .Should().BeEquivalentTo(Tree);
        (await Annotations.FindActiveAnnotationKeysAsync(OrgB, Project, DefaultView, keys, TestContext.Current.CancellationToken))
            .Should().BeEquivalentTo(TreeOfB);

        // the relation arrays are the organization's own: B's second usage is not in A's subject
        ((Subject)(await Annotations.GetAnnotationAsync(Key(OrgA, SubjectKey)))!).ResponsibilityNames.Should().BeEquivalentTo("iso-r");
        ((Subject)(await Annotations.GetAnnotationAsync(Key(OrgB, SubjectKey)))!).ResponsibilityNames.Should().BeEquivalentTo("iso-r", "iso-rb");
    }

    [Fact]
    public async Task Listings_OfOneOrganization_ListOnlyItsAnnotations()
    {
        await SeedAsync();

        // without a responsibility or a subject: organization-wide queries of every type, in one page and in pages of two
        foreach (var service in new IAnnotationService[] { Annotations, Paged })
        {
            var ofA = await ReadAllAsync(service, Request(OrgA));
            KeysOf(ofA).Should().OnlyHaveUniqueItems();
            KeysOf(ofA).Should().BeEquivalentTo(Tree);
            ofA.Should().OnlyContain(annotation => IsOwn(annotation, MarkerA));

            var ofB = await ReadAllAsync(service, Request(OrgB));
            KeysOf(ofB).Should().OnlyHaveUniqueItems();
            KeysOf(ofB).Should().BeEquivalentTo(TreeOfB);
            ofB.Should().OnlyContain(annotation => IsOwn(annotation, MarkerB));
        }

        // with a responsibility or a subject: one partition, under the full key of the organization
        var ofResponsibility = await ReadAllAsync(Paged, Request(OrgA, PartitionKeys.GetResponsibility(Project, "iso-r")));
        KeysOf(ofResponsibility).Should().BeEquivalentTo("rst.iso-r", "unt.iso-r.iso-u", "usg.iso-s.iso-r", "exe.iso-s.iso-r.iso-c", "uxe.iso-s.iso-r.iso-c.iso-u");
        ofResponsibility.Should().OnlyContain(annotation => IsOwn(annotation, MarkerA));

        var ofSubject = await ReadAllAsync(Paged, Request(OrgA, PartitionKeys.GetSubject(Project, "iso-s")));
        KeysOf(ofSubject).Should().BeEquivalentTo("sbt.iso-s", "cnt.iso-s.iso-c");
        ofSubject.Should().OnlyContain(annotation => IsOwn(annotation, MarkerA));

        (await ReadAllAsync(Annotations, Request(OrgA, PartitionKeys.GetResponsibility(Project, "iso-rb"))))
            .Should().BeEmpty("the partition of B's second responsibility has nothing of A");
        KeysOf(await ReadAllAsync(Annotations, Request(OrgB, PartitionKeys.GetResponsibility(Project, "iso-rb"))))
            .Should().BeEquivalentTo("rst.iso-rb", "usg.iso-s.iso-rb");

        // with the deleted and inactive annotations too
        var everything = Request(OrgA, deleted: AnnotationDeletedFilter.Include);
        everything.IncludeInactive = true;
        var all = await ReadAllAsync(Paged, everything);
        KeysOf(all).Should().BeEquivalentTo(Tree);
        all.Should().OnlyContain(annotation => IsOwn(annotation, MarkerA));

        // the export of a view reads every partition of the project
        var exported = await Annotations.GetViewAnnotationsAsync(OrgA, Project, DefaultView, TestContext.Current.CancellationToken);
        KeysOf(exported).Should().BeEquivalentTo(Tree);
        exported.Should().OnlyContain(annotation => IsOwn(annotation, MarkerA));
    }

    [Fact]
    public async Task Descendants_OfOneOrganization_AreItsOwn()
    {
        await SeedAsync();

        // the descendants of a subject and of a context span the partitions of the responsibilities: organization-wide
        var ofSubject = await DescendantsAsync(OrgA, SubjectKey);
        KeysOf(ofSubject).Should().BeEquivalentTo("cnt.iso-s.iso-c", "usg.iso-s.iso-r", "exe.iso-s.iso-r.iso-c", "uxe.iso-s.iso-r.iso-c.iso-u");
        ofSubject.Should().OnlyContain(annotation => IsOwn(annotation, MarkerA));

        var ofSubjectInB = await DescendantsAsync(OrgB, SubjectKey);
        KeysOf(ofSubjectInB).Should().BeEquivalentTo("cnt.iso-s.iso-c", "usg.iso-s.iso-r", "usg.iso-s.iso-rb", "exe.iso-s.iso-r.iso-c", "uxe.iso-s.iso-r.iso-c.iso-u");
        ofSubjectInB.Should().OnlyContain(annotation => IsOwn(annotation, MarkerB));

        var ofContext = await DescendantsAsync(OrgA, "cnt.iso-s.iso-c");
        KeysOf(ofContext).Should().BeEquivalentTo("exe.iso-s.iso-r.iso-c", "uxe.iso-s.iso-r.iso-c.iso-u");
        ofContext.Should().OnlyContain(annotation => IsOwn(annotation, MarkerA));

        // the descendants of a responsibility and of a unit are in one partition of the organization
        var ofResponsibility = await DescendantsAsync(OrgA, "rst.iso-r");
        KeysOf(ofResponsibility).Should().BeEquivalentTo("unt.iso-r.iso-u", "usg.iso-s.iso-r", "exe.iso-s.iso-r.iso-c", "uxe.iso-s.iso-r.iso-c.iso-u");
        ofResponsibility.Should().OnlyContain(annotation => IsOwn(annotation, MarkerA));

        var ofUnit = await DescendantsAsync(OrgA, "unt.iso-r.iso-u");
        KeysOf(ofUnit).Should().Equal("uxe.iso-s.iso-r.iso-c.iso-u");
        ofUnit.Should().OnlyContain(annotation => IsOwn(annotation, MarkerA));

        (await DescendantsAsync(OrgA, "rst.iso-rb")).Should().BeEmpty();
    }

    [Fact]
    public async Task Views_OfOneOrganization_AreItsOwn()
    {
        // the same view registered in both is no conflict: each registration is an item of its organization
        await Views.CreateViewAsync(OrgA, Project, new ViewCreate { Name = ProdView, Description = "a views" }, ActorA);
        await Views.CreateViewAsync(OrgB, Project, new ViewCreate { Name = ProdView, Description = "b views" }, ActorB);
        await Views.CreateViewAsync(OrgB, Project, new ViewCreate { Name = "only-b" }, ActorB);
        await Annotations.CreateAnnotationAsync(OrgA, Model(MarkerA, "rst.iso-r", "data-a"), ActorA);
        await Annotations.CreateAnnotationAsync(OrgB, Model(MarkerB, "rst.iso-r", "data-b"), ActorB);
        var viewsOfB = await Views.GetViewsAsync(OrgB, Project, discover: true);

        var registered = await Views.GetViewsAsync(OrgA, Project, discover: false);
        registered.Select(view => (view.Name, view.Description)).Should().Equal((DefaultView, (string?)null), (ProdView, "a views"));
        registered[1].Created!.By.Should().Be(ActorA);

        // the discovery reads every partition of the organization: the views that hold A's data, never B's
        var discovered = await Views.GetViewsAsync(OrgA, Project, discover: true);
        discovered.Select(view => view.Name).Should().Equal(DefaultView, "data-a", ProdView);

        // an update through A describes or registers A's views only, also one that only B has registered
        await Views.UpdateViewAsync(OrgA, Project, ProdView, new ViewUpdate { Description = "a changed" }, ActorA);
        await Views.UpdateViewAsync(OrgA, Project, "only-b", new ViewUpdate { Description = "a registers it" }, ActorA);

        var updated = await Views.GetViewsAsync(OrgA, Project, discover: false);
        updated.Select(view => (view.Name, view.Description)).Should().Equal(
            (DefaultView, (string?)null),
            ("only-b", "a registers it"),
            (ProdView, "a changed"));
        updated.Where(view => view.IsRegistered).Should().OnlyContain(view => view.Created!.By == ActorA);

        var viewsOfBAfter = await Views.GetViewsAsync(OrgB, Project, discover: true);
        viewsOfBAfter.Should().BeEquivalentTo(viewsOfB);
        viewsOfBAfter.Select(view => (view.Name, view.Description)).Should().Equal(
            (DefaultView, (string?)null),
            ("data-b", (string?)null),
            ("only-b", (string?)null),
            (ProdView, "b views"));
        viewsOfBAfter.Where(view => view.IsRegistered).Should().OnlyContain(view => view.Created!.By == ActorB);
    }

    [Fact]
    public async Task Writes_ThroughOneOrganization_LeaveTheOtherOne()
    {
        await SeedAsync();
        var before = await SnapshotAsync(OrgB);

        await Annotations.SetAnnotationAsync(OrgA, Model(MarkerA, SubjectKey) with { Title = "a:set" }, ActorA);
        await Annotations.UpdateAnnotationAsync(OrgA, Model(MarkerA, "rst.iso-r") with { Title = "a:updated" }, ActorA);
        await Annotations.PatchAnnotationAsync(
            Key(OrgA, "cnt.iso-s.iso-c"),
            AnnotationPatch.Merge(new JsonObject { ["Title"] = "a:patched" }),
            ActorA,
            AnnotationWriteCondition.None);

        // a create under ancestors both organizations have writes the relation arrays of A's ancestors only
        var created = await Annotations.CreateAnnotationAsync(OrgA, Model(MarkerA, "uxe.iso-s.iso-r.iso-c.iso-u2"), ActorA);
        KeysOf(created).Should().BeEquivalentTo(CreatedInA);

        // a key that is active in B only is not active in A, so the bulk create writes it instead of skipping it
        var bulk = await Annotations.CreateAnnotationsAsync(OrgA, new[] { Model(MarkerA, "rst.iso-rb") }, ActorA);
        KeysOf(bulk).Should().Equal("rst.iso-rb");

        (await SnapshotAsync(OrgB)).Should().Equal(before);

        (await Annotations.GetAnnotationAsync(Key(OrgA, SubjectKey)))!.Title.Should().Be("a:set");
        (await Annotations.GetAnnotationAsync(Key(OrgA, "cnt.iso-s.iso-c")))!.Title.Should().Be("a:patched");
        (await Annotations.GetAnnotationAsync(Key(OrgA, "rst.iso-rb")))!.Title.Should().Be(Title(MarkerA, "rst.iso-rb"));

        var responsibility = (Responsibility)(await Annotations.GetAnnotationAsync(Key(OrgA, "rst.iso-r")))!;
        responsibility.Title.Should().Be("a:updated");
        responsibility.UnitNames.Should().BeEquivalentTo("iso-u", "iso-u2");
        ((Execution)(await Annotations.GetAnnotationAsync(Key(OrgA, "exe.iso-s.iso-r.iso-c")))!).UnitNames.Should().BeEquivalentTo("iso-u", "iso-u2");

        // the audit of the update is A's: the previous title is A's, not B's under the same audit id
        var history = await Annotations.GetAnnotationHistoryAsync(Key(OrgA, SubjectKey));
        history!.Entries.Select(entry => entry.Action).Should().Equal(AnnotationAuditAction.Updated, AnnotationAuditAction.Created);
        history.Entries.Should().OnlyContain(entry => entry.Actor.Id == ActorA.Id);
        var title = history.Entries[0].Fields!.Single(field => field.Field == "Title");
        title.Previous!.ToString().Should().Be(Title(MarkerA, SubjectKey));
        title.Value!.ToString().Should().Be("a:set");
    }

    [Fact]
    public async Task SoftDelete_InOneOrganization_LeavesTheOtherOneActive()
    {
        await SeedAsync();
        var before = await SnapshotAsync(OrgB);

        var deletion = await Annotations.DeleteAnnotationAsync(Key(OrgA, SubjectKey), ActorA);

        // the descendants across the responsibility partitions are A's: B's second usage of the subject is not among them
        deletion.DeletedKeys.Should().BeEquivalentTo(SubjectTree);
        (await SnapshotAsync(OrgB)).Should().Equal(before);

        // the deleted list is an organization-wide query
        var deletedInA = await ListDeletedAsync(OrgA, rootsOnly: false);
        KeysOf(deletedInA).Should().BeEquivalentTo(SubjectTree);
        deletedInA.Should().OnlyContain(annotation => IsOwn(annotation, MarkerA) && annotation.Deletion!.DeletionId == deletion.Deletion.DeletionId);
        KeysOf(await ListDeletedAsync(OrgA, rootsOnly: true)).Should().Equal(SubjectKey);
        (await ListDeletedAsync(OrgB, rootsOnly: false)).Should().BeEmpty();
        (await Annotations.GetDeletedAnnotationsAsync(OrgB, Project, DefaultView, rootsOnly: false, deletionId: deletion.Deletion.DeletionId))
            .Annotations.Should().BeEmpty();

        // the annotation list with its filter of deleted annotations
        KeysOf(await ReadAllAsync(Paged, Request(OrgA, deleted: AnnotationDeletedFilter.Only))).Should().BeEquivalentTo(SubjectTree);
        (await ReadAllAsync(Paged, Request(OrgB, deleted: AnnotationDeletedFilter.Only))).Should().BeEmpty();
        KeysOf(await ReadAllAsync(Paged, Request(OrgA))).Should().BeEquivalentTo(Tree.Except(SubjectTree));
        KeysOf(await ReadAllAsync(Paged, Request(OrgB))).Should().BeEquivalentTo(TreeOfB);

        // the reads of many items
        var keys = KnownKeys.Select(AnnotationKey.Parse).ToList();
        KeysOf(await Annotations.FindDeletedAnnotationsAsync(OrgA, Project, DefaultView, keys)).Should().BeEquivalentTo(SubjectTree);
        (await Annotations.FindDeletedAnnotationsAsync(OrgB, Project, DefaultView, keys)).Should().BeEmpty();
        (await Annotations.FindActiveAnnotationKeysAsync(OrgA, Project, DefaultView, keys, TestContext.Current.CancellationToken))
            .Should().BeEquivalentTo(Tree.Except(SubjectTree));
        (await Annotations.FindActiveAnnotationKeysAsync(OrgB, Project, DefaultView, keys, TestContext.Current.CancellationToken))
            .Should().BeEquivalentTo(TreeOfB);
    }

    [Fact]
    public async Task Restore_InOneOrganization_LeavesTheOtherOneDeleted()
    {
        await SeedAsync();

        // the same keys deleted in both organizations: two deletions, each of its own organization's items
        var deletionOfB = await Annotations.DeleteAnnotationAsync(Key(OrgB, SubjectKey), ActorB);
        var deletionOfA = await Annotations.DeleteAnnotationAsync(Key(OrgA, SubjectKey), ActorA);

        deletionOfB.DeletedKeys.Should().BeEquivalentTo(SubjectTreeOfB);
        deletionOfA.DeletedKeys.Should().BeEquivalentTo(SubjectTree, "B's deletion of the same keys is not A's");
        deletionOfA.Deletion.DeletionId.Should().NotBe(deletionOfB.Deletion.DeletionId);
        var before = await SnapshotAsync(OrgB);

        var restored = await Annotations.RestoreAnnotationsAsync(OrgA, Project, DefaultView, [AnnotationKey.Parse(SubjectKey)], ActorA, includeDescendants: true);

        // the descendants of the restore are A's: B's deleted second usage is not brought back into A
        restored.Failed.Should().BeEmpty();
        restored.CreatedKeys.Should().BeEmpty();
        KeysOf(restored.Annotations).Should().BeEquivalentTo(SubjectTree);
        restored.Annotations.Should().OnlyContain(annotation => IsOwn(annotation, MarkerA));
        (await SnapshotAsync(OrgB)).Should().Equal(before);

        (await ListDeletedAsync(OrgA, rootsOnly: false)).Should().BeEmpty();
        KeysOf(await ListDeletedAsync(OrgB, rootsOnly: false)).Should().BeEquivalentTo(SubjectTreeOfB);
        KeysOf(await ListDeletedAsync(OrgB, rootsOnly: true)).Should().Equal(SubjectKey);
        KeysOf(await ReadAllAsync(Annotations, Request(OrgA))).Should().BeEquivalentTo(Tree);
        ((Subject)(await Annotations.GetAnnotationAsync(Key(OrgA, SubjectKey)))!).ResponsibilityNames.Should().BeEquivalentTo("iso-r");
        (await Annotations.GetAnnotationAsync(Key(OrgB, SubjectKey))).Should().BeNull();

        var historyOfA = await Annotations.GetAnnotationHistoryAsync(Key(OrgA, SubjectKey));
        historyOfA!.State.Should().Be(AnnotationState.Active);
        historyOfA.Entries.Select(entry => entry.Action).Should().Equal(AnnotationAuditAction.Restored, AnnotationAuditAction.Deleted, AnnotationAuditAction.Created);
        historyOfA.Entries.Should().OnlyContain(entry => entry.Actor.Id == ActorA.Id);

        var historyOfB = await Annotations.GetAnnotationHistoryAsync(Key(OrgB, SubjectKey));
        historyOfB!.State.Should().Be(AnnotationState.Deleted);
        historyOfB.Entries.Select(entry => entry.Action).Should().NotContain(AnnotationAuditAction.Restored);
        historyOfB.Entries.Should().OnlyContain(entry => entry.Actor.Id == ActorB.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Purge_InOneOrganization_LeavesTheOtherOne(bool deletedInB)
    {
        await SeedAsync();

        // with the same keys deleted in B too, the purge's query of deleted descendants could reach B's
        if (deletedInB)
        {
            await Annotations.DeleteAnnotationAsync(Key(OrgB, SubjectKey), ActorB);
        }

        await Annotations.DeleteAnnotationAsync(Key(OrgA, SubjectKey), ActorA);
        var before = await SnapshotAsync(OrgB);

        var purged = await Annotations.PurgeAnnotationAsync(Key(OrgA, SubjectKey), ActorA);

        purged.PurgedKeys.Should().BeEquivalentTo(SubjectTree);
        (await SnapshotAsync(OrgB)).Should().Equal(before);

        foreach (var key in SubjectTree)
        {
            (await Annotations.GetDeletedAnnotationAsync(Key(OrgA, key))).Should().BeNull();
            (await Annotations.GetAnnotationHistoryAsync(Key(OrgA, key))).Should().BeNull("the purge removes A's audits");
            (await Annotations.GetAnnotationHistoryAsync(Key(OrgB, key))).Should().NotBeNull("B's audits under the same ids stay");
        }

        (await ListDeletedAsync(OrgA, rootsOnly: false)).Should().BeEmpty();
        KeysOf(await ReadAllAsync(Annotations, Request(OrgA))).Should().BeEquivalentTo(Tree.Except(SubjectTree));

        if (deletedInB)
        {
            KeysOf(await ListDeletedAsync(OrgB, rootsOnly: false)).Should().BeEquivalentTo(SubjectTreeOfB);
            (await Annotations.GetDeletedAnnotationAsync(Key(OrgB, SubjectKey)))!.Title.Should().Be(Title(MarkerB, SubjectKey));
        }
        else
        {
            (await ListDeletedAsync(OrgB, rootsOnly: false)).Should().BeEmpty();
            KeysOf(await ReadAllAsync(Annotations, Request(OrgB))).Should().BeEquivalentTo(TreeOfB);
        }
    }

    private static string Title(string marker, string key) => $"{marker}:{key}";

    // The annotation carries the title its own organization wrote.
    private static bool IsOwn(Annotation annotation, string marker) => annotation.Title == Title(marker, annotation.AnnotationKey);

    private static Annotation Model(string marker, string key, string view = DefaultView)
    {
        return AnnotationFactory.CreateDefault(AnnotationKey.Parse(key), Project, view) with
        {
            Title = Title(marker, key),
            Values = new Dictionary<string, object> { ["owner"] = marker },
        };
    }

    private static FullKey Key(string organization, string key)
    {
        return FullKey.Create(AnnotationKey.Parse(key), organization, Project, DefaultView);
    }

    private static List<string> KeysOf(IEnumerable<Annotation> annotations)
    {
        return annotations.Select(annotation => annotation.AnnotationKey).ToList();
    }

    private static AnnotationsRequest Request(string organization, string? partitionKey = null, AnnotationDeletedFilter deleted = AnnotationDeletedFilter.Exclude)
    {
        return new AnnotationsRequest
        {
            Organization = organization,
            Project = Project,
            View = DefaultView,
            Types = Enum.GetValues<AnnotationType>(),
            PartitionKey = partitionKey,
            Deleted = deleted,
        };
    }

    // Follows the continuation token to the end, as a client does.
    private static async Task<List<Annotation>> ReadAllAsync(IAnnotationService service, AnnotationsRequest request)
    {
        var annotations = new List<Annotation>();
        var pages = 0;

        do
        {
            pages++;
            pages.Should().BeLessThanOrEqualTo(MaxPages, "the list ends");

            var page = await service.GetAnnotationsAsync(request);
            annotations.AddRange(page.Annotations);
            request.ContinuationToken = page.ContinuationToken;
        }
        while (request.ContinuationToken is not null);

        return annotations;
    }

    private static string Serialize(Annotation? annotation)
    {
        return annotation is null ? "none" : JsonSerializer.Serialize(annotation, annotation.GetType());
    }

    // The same tree in both organizations, A and B in turn per key, each annotation with the title of its organization;
    // then B's second responsibility and its usage of the shared subject.
    private async Task SeedAsync()
    {
        foreach (var key in Tree)
        {
            await Annotations.CreateAnnotationAsync(OrgA, Model(MarkerA, key), ActorA);
            await Annotations.CreateAnnotationAsync(OrgB, Model(MarkerB, key), ActorB);
        }

        foreach (var key in OnlyInB)
        {
            await Annotations.CreateAnnotationAsync(OrgB, Model(MarkerB, key), ActorB);
        }
    }

    private Task<List<Annotation>> DescendantsAsync(string organization, string key)
    {
        var request = Request(organization);
        request.DescendantsOf = AnnotationKey.Parse(key);
        return ReadAllAsync(Annotations, request);
    }

    private async Task<List<Annotation>> ListDeletedAsync(string organization, bool rootsOnly)
    {
        var annotations = new List<Annotation>();
        string? token = null;
        var pages = 0;

        do
        {
            pages++;
            pages.Should().BeLessThanOrEqualTo(MaxPages, "the list ends");

            var page = await Annotations.GetDeletedAnnotationsAsync(organization, Project, DefaultView, rootsOnly, continuationToken: token);
            annotations.AddRange(page.Annotations);
            token = page.ContinuationToken;
        }
        while (token is not null);

        return annotations;
    }

    // What a reader of the organization sees of its annotations: the point reads and the history of every key the tests
    // use, the list with the deleted and inactive annotations, and the deleted list. Equal snapshots: nothing changed
    // (a write changes the Modified stamp of an annotation, an audit entry adds its id).
    private async Task<List<string>> SnapshotAsync(string organization)
    {
        var lines = new List<string>();

        foreach (var key in KnownKeys)
        {
            var fullKey = Key(organization, key);
            var history = await Annotations.GetAnnotationHistoryAsync(fullKey);

            lines.Add($"{key} active {Serialize(await Annotations.GetAnnotationAsync(fullKey))}");
            lines.Add($"{key} deleted {Serialize(await Annotations.GetDeletedAnnotationAsync(fullKey))}");
            lines.Add(history is null
                ? $"{key} history none"
                : $"{key} history {history.State} {string.Join(",", history.Entries.Select(entry => entry.Id))}");
        }

        var everything = Request(organization, deleted: AnnotationDeletedFilter.Include);
        everything.IncludeInactive = true;
        lines.AddRange((await ReadAllAsync(Annotations, everything))
            .Select(annotation => $"listed {annotation.AnnotationKey} {annotation.Title}")
            .Order(StringComparer.Ordinal));
        lines.AddRange((await ListDeletedAsync(organization, rootsOnly: false))
            .Select(annotation => $"deleted {annotation.AnnotationKey} {annotation.Deletion!.DeletionId}")
            .Order(StringComparer.Ordinal));

        return lines;
    }
}
