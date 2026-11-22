#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Entities.Inbox;
using _42.Platform.Storyteller.Notifying;
using _42.Platform.Storyteller.Transferring;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The store of the inbox of a view (IViewInboxStore) on Cosmos DB: the create and its conflict, the point read through
// the view, the replace guarded by the ETag, the list and the count per role, and the ttl of every write, read from the
// stored document. Every test writes into a project of its own, and none waits for an expiry.
public class CosmosViewInboxStoreTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Org = TestConstants.Organization;
    private const string View = Constants.DefaultViewName;
    private const string OtherView = "prod";

    private static readonly DateTimeOffset Start = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);
    private static readonly AuditActor Author = AuditActor.Machine("ci-export", "Nightly export");
    private static readonly AuditActor Closer = AuditActor.Account("user_ada", "Ada") with { Via = "mcp" };

    private static readonly AccountRole[] EveryRole =
    [
        AccountRole.Contributor,
        AccountRole.ContributorWithSecrets,
        AccountRole.Administrator,
        AccountRole.Owner,
    ];

    // The items of the list and the count, by the minute of their creation: the role that sees each and its state.
    private static readonly (int Minute, AccountRole Role, ViewInboxItemState State)[] Mixed =
    [
        (1, AccountRole.Contributor, ViewInboxItemState.Open),
        (2, AccountRole.Administrator, ViewInboxItemState.Open),
        (3, AccountRole.Contributor, ViewInboxItemState.Dismissed),
        (4, AccountRole.Owner, ViewInboxItemState.Open),
        (5, AccountRole.Administrator, ViewInboxItemState.Accepted),
        (6, AccountRole.Contributor, ViewInboxItemState.Open),
        (7, AccountRole.ContributorWithSecrets, ViewInboxItemState.Open),
        (8, AccountRole.Owner, ViewInboxItemState.Dismissed),
    ];

    private IViewInboxStore Store => Context.Services.GetRequiredService<IViewInboxStore>();

    private Container Container => Context.Services.GetRequiredService<IContainerRepositoryProvider>().GetOrganizationContainer(Org).Container;

    [Fact]
    public async Task Create_StoresTheItemWithEveryMember_UnderItsView()
    {
        var project = NewProject("create");
        var createdAt = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.FromHours(2)).AddTicks(1234567);
        var record = new ViewInboxRecord
        {
            Organization = Org,
            Project = project,
            View = View,
            Item = new ViewInboxItem
            {
                Id = ViewInboxIds.NewId(createdAt),
                Kind = ViewInboxItemKind.Action,
                Topic = "SchemaSuggestion",
                Level = ViewInboxItemLevel.Warning,
                Title = "Generate a schema for 4 similar configurations",
                Text = "rst.payments, rst.billing, rst.shipping, rst.returns",
                MinimalRole = AccountRole.ContributorWithSecrets,
                State = ViewInboxItemState.Accepted,
                CreatedAt = createdAt,
                CreatedBy = AuditActor.System,
                ExpiresAt = createdAt.AddDays(30),
                Closed = AuditStamp.Of(Closer, createdAt.AddMinutes(5)),
                Action = "GenerateSchema",
                Job = new ViewInboxJob { Kind = ViewInboxJobKind.ViewImport, Id = Guid.NewGuid().ToString("N"), FileName = "acme.zip" },
            },
        };

        var created = await Store.TryCreateAsync(record, Lifetime);

        created.Should().NotBeNull();
        created!.ETag.Should().NotBeNullOrEmpty();
        created.Should().BeEquivalentTo(record with { ETag = created.ETag });

        // both read paths give the item back: the point read with its ETag, the list without one
        (await Store.GetAsync(Org, project, View, record.Item.Id)).Should().BeEquivalentTo(created);
        var listed = await Store.QueryAsync(Org, project, View, ViewInboxFilter.Done, [AccountRole.ContributorWithSecrets], before: null, take: 10);
        listed.Should().ContainSingle().Which.Should().BeEquivalentTo(record);
        listed[0].ETag.Should().BeNull();

        var raw = await ReadRawAsync(project, View, record.Item.Id);
        raw.GetProperty("id").GetString().Should().Be($"inb.{record.Item.Id}");
        raw.GetProperty("PartitionKey").GetString().Should().Be($"{project}.inbox.{View}");
        raw.GetProperty("Organization").GetString().Should().Be(Org);
        raw.GetProperty("Project").GetString().Should().Be(project);
        raw.GetProperty("View").GetString().Should().Be(View);
        raw.GetProperty("ItemId").GetString().Should().Be(record.Item.Id);

        // the names the list and the count compare
        raw.GetProperty("MinimalRole").GetString().Should().Be("ContributorWithSecrets");
        raw.GetProperty("State").GetString().Should().Be("Accepted");

        // a ViewName would make the view a discovered one
        raw.TryGetProperty("ViewName", out _).Should().BeFalse();
        raw.TryGetProperty("ProjectName", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Create_ATakenId_ReturnsNullAndKeepsTheStoredItem()
    {
        var project = NewProject("conflict");
        var first = await Store.TryCreateAsync(Record(project), TimeSpan.FromHours(2));
        var again = first! with { ETag = null, Item = first.Item with { Title = "Written twice" } };

        var second = await Store.TryCreateAsync(again, TimeSpan.FromHours(1));

        second.Should().BeNull();
        (await Store.GetAsync(Org, project, View, first.Item.Id)).Should().BeEquivalentTo(first);
        (await ReadTimeToLiveAsync(project, View, first.Item.Id)).Should().Be(7200);

        // the id is taken in its view only
        (await Store.TryCreateAsync(again with { View = OtherView }, Lifetime)).Should().NotBeNull();
        (await Store.TryCreateAsync(again with { Project = NewProject("conflict") }, Lifetime)).Should().NotBeNull();
        (await Store.GetAsync(Org, project, View, first.Item.Id))!.Item.Title.Should().Be(first.Item.Title);
    }

    [Fact]
    public async Task Get_FindsAnItemOnlyThroughItsView()
    {
        var project = NewProject("read");
        var created = await Store.TryCreateAsync(Record(project), Lifetime);
        var id = created!.Item.Id;

        var read = await Store.GetAsync(Org, project, View, id);

        read.Should().BeEquivalentTo(created);
        (await Store.GetAsync(Org, project, OtherView, id)).Should().BeNull();
        (await Store.GetAsync(Org, NewProject("read"), View, id)).Should().BeNull();
        (await Store.GetAsync(Org, project, View, ViewInboxIds.NewId(Start))).Should().BeNull();
    }

    [Fact]
    public async Task Get_AnItemOfAnotherViewInTheSamePartition_IsNotFound()
    {
        // names with a dot are older than the rules of names: two pairs of a project and a view can share one partition
        var name = NewProject("dots");
        var (project, view) = ($"{name}.inbox.a", "b");
        var (otherProject, otherView) = (name, "a.inbox.b");
        PartitionKeys.GetInbox(otherProject, otherView).Should().Be(PartitionKeys.GetInbox(project, view));

        var created = await Store.TryCreateAsync(Record(project, view), Lifetime);

        (await Store.GetAsync(Org, project, view, created!.Item.Id)).Should().BeEquivalentTo(created);
        (await Store.GetAsync(Org, otherProject, otherView, created.Item.Id)).Should().BeNull();

        // the list and the count look at the names as well
        (await Store.QueryAsync(Org, project, view, ViewInboxFilter.Open, EveryRole, before: null, take: 10)).Should().ContainSingle();
        (await Store.QueryAsync(Org, otherProject, otherView, ViewInboxFilter.Open, EveryRole, before: null, take: 10)).Should().BeEmpty();
        (await Store.CountOpenAsync(Org, project, view, EveryRole)).Should().Be(1);
        (await Store.CountOpenAsync(Org, otherProject, otherView, EveryRole)).Should().Be(0);
    }

    [Fact]
    public async Task Replace_SavesOnlyWithTheCurrentETag()
    {
        var project = NewProject("replace");
        var created = await Store.TryCreateAsync(Record(project), Lifetime);
        var id = created!.Item.Id;
        var stamp = AuditStamp.Of(Closer, Start.AddMinutes(5));

        var dismissed = await Store.TryReplaceAsync(
            created with { Item = created.Item with { State = ViewInboxItemState.Dismissed, Closed = stamp } },
            Lifetime);

        dismissed.Should().NotBeNull();
        dismissed!.ETag.Should().NotBeNullOrEmpty().And.NotBe(created.ETag);
        dismissed.Item.Should().BeEquivalentTo(created.Item with { State = ViewInboxItemState.Dismissed, Closed = stamp });
        (await Store.GetAsync(Org, project, View, id)).Should().BeEquivalentTo(dismissed);

        // the ETag of the create is stale now
        var late = await Store.TryReplaceAsync(
            created with { Item = created.Item with { State = ViewInboxItemState.Accepted, Closed = AuditStamp.Of(Author, Start.AddMinutes(6)) } },
            Lifetime);

        late.Should().BeNull();
        (await Store.GetAsync(Org, project, View, id)).Should().BeEquivalentTo(dismissed);

        // the ETag of the last write saves again, and a member that is null now is gone from the stored item
        var reopened = await Store.TryReplaceAsync(
            dismissed with { Item = dismissed.Item with { State = ViewInboxItemState.Open, Closed = null } },
            Lifetime);

        reopened.Should().NotBeNull();
        var read = await Store.GetAsync(Org, project, View, id);
        read!.ETag.Should().Be(reopened!.ETag);
        read.Item.Should().BeEquivalentTo(created.Item);
    }

    [Fact]
    public async Task Replace_AnItemThatIsGone_ReturnsNull()
    {
        var project = NewProject("gone");
        var created = await Store.TryCreateAsync(Record(project), Lifetime);
        var id = created!.Item.Id;

        // what the expiry does
        using (var deleted = await Container.DeleteItemStreamAsync(ViewInboxEntity.GetId(id), PartitionKeys.GetCosmosInbox(Org, project, View)))
        {
            deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var replaced = await Store.TryReplaceAsync(created with { Item = created.Item with { State = ViewInboxItemState.Dismissed } }, Lifetime);

        replaced.Should().BeNull();
        (await Store.GetAsync(Org, project, View, id)).Should().BeNull("a replace never creates the item again");
    }

    [Fact]
    public async Task Replace_WithoutAnETag_IsRefused()
    {
        var project = NewProject("no-etag");
        var created = await Store.TryCreateAsync(Record(project), Lifetime);
        var listed = (await Store.QueryAsync(Org, project, View, ViewInboxFilter.Open, EveryRole, before: null, take: 10)).Single();
        listed.ETag.Should().BeNull();

        var replace = () => Store.TryReplaceAsync(listed with { Item = listed.Item with { State = ViewInboxItemState.Dismissed } }, Lifetime);

        await replace.Should().ThrowAsync<ArgumentException>();
        (await Store.GetAsync(Org, project, View, created!.Item.Id)).Should().BeEquivalentTo(created);
    }

    [Fact]
    public async Task Query_ReturnsTheItemsOfTheFilterAndTheRoles_NewestFirst()
    {
        var project = NewProject("query");
        await SeedMixedAsync(project);

        (await QueryMinutesAsync(project, View, ViewInboxFilter.Open, AccountRole.Contributor)).Should().Equal(6, 1);
        (await QueryMinutesAsync(project, View, ViewInboxFilter.Open, AccountRole.Contributor, AccountRole.ContributorWithSecrets, AccountRole.Administrator))
            .Should().Equal(7, 6, 2, 1);
        (await QueryMinutesAsync(project, View, ViewInboxFilter.Open, EveryRole)).Should().Equal(7, 6, 4, 2, 1);

        (await QueryMinutesAsync(project, View, ViewInboxFilter.Done, AccountRole.Contributor)).Should().Equal(3);
        (await QueryMinutesAsync(project, View, ViewInboxFilter.Done, AccountRole.Contributor, AccountRole.ContributorWithSecrets, AccountRole.Administrator))
            .Should().Equal(5, 3);
        (await QueryMinutesAsync(project, View, ViewInboxFilter.Done, EveryRole)).Should().Equal(8, 5, 3);

        // the roles are taken as they are given: the store knows no order of them
        (await QueryMinutesAsync(project, View, ViewInboxFilter.Open, AccountRole.Administrator)).Should().Equal(2);
        (await QueryMinutesAsync(project, View, ViewInboxFilter.Open, AccountRole.Owner, AccountRole.Contributor)).Should().Equal(6, 4, 1);
        (await QueryMinutesAsync(project, View, ViewInboxFilter.Open)).Should().BeEmpty();
        (await QueryMinutesAsync(project, View, ViewInboxFilter.Open, AccountRole.Reader)).Should().BeEmpty();

        // every view has an inbox of its own
        (await QueryMinutesAsync(project, OtherView, ViewInboxFilter.Open, EveryRole)).Should().Equal(9);
        (await QueryMinutesAsync(project, OtherView, ViewInboxFilter.Done, EveryRole)).Should().BeEmpty();
        (await QueryMinutesAsync(project, "empty", ViewInboxFilter.Open, EveryRole)).Should().BeEmpty();
    }

    [Fact]
    public async Task Query_WithBefore_ContinuesBelowThatItem()
    {
        var project = NewProject("paging");
        AccountRole[] roles = [AccountRole.Administrator];
        var ids = new Dictionary<int, string>();

        // seven open items and two closed ones between them, stored out of their order
        foreach (var minute in new[] { 4, 9, 1, 6, 3, 8, 2, 7, 5 })
        {
            var state = minute is 3 or 7 ? ViewInboxItemState.Dismissed : ViewInboxItemState.Open;
            var created = await Store.TryCreateAsync(Record(project, minute: minute, state: state), Lifetime);
            ids[minute] = created!.Item.Id;
        }

        var first = await Store.QueryAsync(Org, project, View, ViewInboxFilter.Open, roles, before: null, take: 3);
        var second = await Store.QueryAsync(Org, project, View, ViewInboxFilter.Open, roles, before: first[^1].Item.Id, take: 3);
        var third = await Store.QueryAsync(Org, project, View, ViewInboxFilter.Open, roles, before: second[^1].Item.Id, take: 3);
        var fourth = await Store.QueryAsync(Org, project, View, ViewInboxFilter.Open, roles, before: third[^1].Item.Id, take: 3);

        Minutes(first).Should().Equal(9, 8, 6);
        Minutes(second).Should().Equal(5, 4, 2);
        Minutes(third).Should().Equal(1);
        fourth.Should().BeEmpty();

        // before is a place in the order: the id of an item the filter leaves out, or of no item at all
        Minutes(await Store.QueryAsync(Org, project, View, ViewInboxFilter.Open, roles, before: ids[7], take: 10)).Should().Equal(6, 5, 4, 2, 1);
        Minutes(await Store.QueryAsync(Org, project, View, ViewInboxFilter.Open, roles, before: ViewInboxIds.NewId(Start.AddSeconds(150)), take: 10))
            .Should().Equal(2, 1);
        Minutes(await Store.QueryAsync(Org, project, View, ViewInboxFilter.Done, roles, before: ids[7], take: 10)).Should().Equal(3);

        // take is the most, not a promise
        Minutes(await Store.QueryAsync(Org, project, View, ViewInboxFilter.Done, roles, before: null, take: 1)).Should().Equal(7);
        Minutes(await Store.QueryAsync(Org, project, View, ViewInboxFilter.Open, roles, before: null, take: 100)).Should().Equal(9, 8, 6, 5, 4, 2, 1);
    }

    [Fact]
    public async Task CountOpen_CountsTheOpenItemsOfTheRoles()
    {
        var project = NewProject("count");
        var ids = await SeedMixedAsync(project);

        (await Store.CountOpenAsync(Org, project, View, [AccountRole.Contributor])).Should().Be(2);
        (await Store.CountOpenAsync(Org, project, View, [AccountRole.Contributor, AccountRole.ContributorWithSecrets, AccountRole.Administrator])).Should().Be(4);
        (await Store.CountOpenAsync(Org, project, View, EveryRole)).Should().Be(5);
        (await Store.CountOpenAsync(Org, project, View, [AccountRole.Owner])).Should().Be(1);
        (await Store.CountOpenAsync(Org, project, View, [])).Should().Be(0);

        (await Store.CountOpenAsync(Org, project, OtherView, EveryRole)).Should().Be(1);
        (await Store.CountOpenAsync(Org, project, OtherView, [AccountRole.Administrator])).Should().Be(0);
        (await Store.CountOpenAsync(Org, project, "empty", EveryRole)).Should().Be(0);

        // an item that is closed leaves the count
        var open = await Store.GetAsync(Org, project, View, ids[6]);
        var dismissed = await Store.TryReplaceAsync(
            open! with { Item = open.Item with { State = ViewInboxItemState.Dismissed, Closed = AuditStamp.Of(Closer, Start.AddMinutes(10)) } },
            Lifetime);

        dismissed.Should().NotBeNull();
        (await Store.CountOpenAsync(Org, project, View, [AccountRole.Contributor])).Should().Be(1);
        (await Store.CountOpenAsync(Org, project, View, EveryRole)).Should().Be(4);
        (await QueryMinutesAsync(project, View, ViewInboxFilter.Done, AccountRole.Contributor)).Should().Equal(6, 3);
    }

    [Theory]
    [InlineData(-30, 1)]
    [InlineData(0, 1)]
    [InlineData(0.2, 1)]
    [InlineData(1, 1)]
    [InlineData(89.1, 90)]
    [InlineData(90, 90)]
    [InlineData(365 * 24 * 60 * 60, 31536000)]
    public void TimeToLive_IsRoundedUp_AndAtLeastASecond(double seconds, int expected)
    {
        CosmosViewInboxStore.GetTimeToLive(TimeSpan.FromSeconds(seconds)).Should().Be(expected);
    }

    [Fact]
    public async Task StoredTtl_IsTheTimeToLiveOfEachWrite()
    {
        var project = NewProject("ttl");
        var created = await Store.TryCreateAsync(Record(project), TimeSpan.FromSeconds(7199.4));
        var id = created!.Item.Id;

        (await ReadTimeToLiveAsync(project, View, id)).Should().Be(7200);

        var dismissed = await Store.TryReplaceAsync(
            created with { Item = created.Item with { State = ViewInboxItemState.Dismissed, Closed = AuditStamp.Of(Closer, Start.AddMinutes(30)) } },
            TimeSpan.FromMinutes(90));

        dismissed.Should().NotBeNull();
        (await ReadTimeToLiveAsync(project, View, id)).Should().Be(5400);

        // a lost replace writes nothing, its time to live included
        (await Store.TryReplaceAsync(created, TimeSpan.FromMinutes(5))).Should().BeNull();
        (await ReadTimeToLiveAsync(project, View, id)).Should().Be(5400);
    }

    [Fact]
    public async Task StoredTtl_OfAPushedItem_IsTheTimeItStillHas_AndADismissDoesNotRestartIt()
    {
        var project = NewProject("expiry");
        var clock = new Clock(Start);
        var publisher = new ViewInboxPublisher(Store, Options.Create(new ViewInboxOptions()), clock, NullLogger<ViewInboxPublisher>.Instance);
        var service = new ViewInboxService(Store, [], clock, NullLogger<ViewInboxService>.Instance);

        var pushed = await publisher.PushAsync(new ViewInboxPush
        {
            Organization = Org,
            Project = project,
            View = View,
            Kind = ViewInboxItemKind.Notification,
            Topic = ViewInboxTopics.ViewExport,
            Level = ViewInboxItemLevel.Success,
            Title = "Export finished",
            MinimalRole = AccountRole.Administrator,
            CreatedBy = Author,
            ExpiresAt = Start.AddHours(2),
        });

        (await ReadTimeToLiveAsync(project, View, pushed.Id)).Should().Be(7200);

        // half an hour later the item has an hour and a half left, and the write of the dismiss gives it no more
        clock.Now = Start.AddMinutes(30);
        var dismissed = await service.DismissAsync(Org, project, View, pushed.Id, AccountRole.Administrator, Closer);

        dismissed.State.Should().Be(ViewInboxItemState.Dismissed);
        dismissed.ExpiresAt.Should().Be(Start.AddHours(2));
        (await ReadTimeToLiveAsync(project, View, pushed.Id)).Should().Be(5400);
    }

    [Fact]
    public async Task ViewDiscovery_DoesNotFindAViewThatHoldsOnlyInboxItems()
    {
        var project = NewProject("discover");
        var jobId = Guid.NewGuid().ToString("N");
        (await Store.TryCreateAsync(Record(project, "inbox-only"), Lifetime)).Should().NotBeNull();

        // a job has the ViewName the discovery looks for: its view is found, so the project was searched
        await Context.Services.GetRequiredService<IViewTransferStore>().CreateAsync(new ViewTransferRecord
        {
            Organization = Org,
            Project = project,
            View = "jobs-only",
            Kind = ViewTransferKind.Export,
            Id = jobId,
            Export = new ViewExport
            {
                Id = jobId,
                Status = ViewTransferStatus.Succeeded,
                Format = ViewArchiveFormat.Git,
                FileName = "configurations.zip",
                CreatedAt = Start,
                CreatedBy = Author,
                ExpiresAt = Start.AddHours(24),
            },
        });

        var views = await Context.Services.GetRequiredService<IViewService>().GetViewsAsync(Org, project, discover: true);

        views.Select(view => view.Name).Should().Contain("jobs-only").And.NotContain("inbox-only");
    }

    [Fact]
    public async Task Service_ContinuesTheListWithItsToken()
    {
        var project = NewProject("service");
        var service = new ViewInboxService(Store, [], new Clock(Start), NullLogger<ViewInboxService>.Instance);
        var count = ViewInboxQuery.PageSize + 2;
        var ids = new List<string>();

        // two items more than a page, and one that only an Owner sees
        for (var minute = 1; minute <= count; minute++)
        {
            var created = await Store.TryCreateAsync(Record(project, minute: minute), Lifetime);
            ids.Add(created!.Item.Id);
        }

        (await Store.TryCreateAsync(Record(project, minute: count + 1, role: AccountRole.Owner), Lifetime)).Should().NotBeNull();
        ids.Reverse();

        var first = await service.GetItemsAsync(Org, project, View, new ViewInboxQuery { Role = AccountRole.Administrator });
        var second = await service.GetItemsAsync(
            Org,
            project,
            View,
            new ViewInboxQuery { Role = AccountRole.Administrator, ContinuationToken = first.ContinuationToken });

        first.Items.Select(item => item.Id).Should().Equal(ids.Take(ViewInboxQuery.PageSize));
        first.ContinuationToken.Should().NotBeNull();
        first.OpenCount.Should().Be(count);
        second.Items.Select(item => item.Id).Should().Equal(ids.Skip(ViewInboxQuery.PageSize));
        second.ContinuationToken.Should().BeNull();
        second.OpenCount.Should().Be(count);
    }

    private static string NewProject(string name) => $"inbox-{name}-{Guid.NewGuid().ToString("N")[..12]}";

    private static ViewInboxRecord Record(
        string project,
        string view = View,
        int minute = 0,
        AccountRole role = AccountRole.Administrator,
        ViewInboxItemState state = ViewInboxItemState.Open)
    {
        var createdAt = Start.AddMinutes(minute);

        return new ViewInboxRecord
        {
            Organization = Org,
            Project = project,
            View = view,
            Item = new ViewInboxItem
            {
                Id = ViewInboxIds.NewId(createdAt),
                Kind = ViewInboxItemKind.Notification,
                Topic = ViewInboxTopics.ViewExport,
                Level = ViewInboxItemLevel.Success,
                Title = $"Export {minute} finished",
                MinimalRole = role,
                State = state,
                CreatedAt = createdAt,
                CreatedBy = Author,
                ExpiresAt = createdAt + Lifetime,
                Closed = state == ViewInboxItemState.Open ? null : AuditStamp.Of(Closer, createdAt.AddSeconds(30)),
            },
        };
    }

    // The minutes of the items, in the order of the list.
    private static IEnumerable<int> Minutes(IEnumerable<ViewInboxRecord> records)
    {
        return records.Select(record => (int)(record.Item.CreatedAt - Start).TotalMinutes);
    }

    // Stores the mixed items in an order that is neither their time order nor its reverse, and one more in the other
    // view. Returns the ids by the minute.
    private async Task<Dictionary<int, string>> SeedMixedAsync(string project)
    {
        var ids = new Dictionary<int, string>();

        foreach (var minute in new[] { 5, 1, 8, 3, 7, 2, 6, 4 })
        {
            var (_, role, state) = Mixed.Single(item => item.Minute == minute);
            var created = await Store.TryCreateAsync(Record(project, minute: minute, role: role, state: state), Lifetime);
            ids[minute] = created!.Item.Id;
        }

        (await Store.TryCreateAsync(Record(project, OtherView, minute: 9, role: AccountRole.Contributor), Lifetime)).Should().NotBeNull();
        return ids;
    }

    private async Task<IEnumerable<int>> QueryMinutesAsync(string project, string view, ViewInboxFilter filter, params AccountRole[] roles)
    {
        return Minutes(await Store.QueryAsync(Org, project, view, filter, roles, before: null, take: 100));
    }

    // The stored document of an item, as Cosmos DB holds it.
    private async Task<JsonElement> ReadRawAsync(string project, string view, string id)
    {
        using var response = await Container.ReadItemStreamAsync(ViewInboxEntity.GetId(id), PartitionKeys.GetCosmosInbox(Org, project, view));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = await JsonDocument.ParseAsync(response.Content);
        return document.RootElement.Clone();
    }

    private async Task<int> ReadTimeToLiveAsync(string project, string view, string id)
    {
        return (await ReadRawAsync(project, view, id)).GetProperty("ttl").GetInt32();
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
