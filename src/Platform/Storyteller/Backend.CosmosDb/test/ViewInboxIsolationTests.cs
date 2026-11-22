#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities.Inbox;
using _42.Platform.Storyteller.Notifying;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Tenant isolation of the inbox of a view in the shared container "organizations": two organizations with the same
// project, view and item ids, and different titles ("alpha" in A, "bravo" in B). Whatever runs through one organization
// reads, counts and writes only its own items. Every test seeds its own pair of organizations.
public class ViewInboxIsolationTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Project = "billing";
    private const string View = Constants.DefaultViewName;
    private const string AlphaMarker = "alpha";
    private const string BravoMarker = "bravo";

    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);
    private static readonly AccountRole[] Roles = [AccountRole.Contributor, AccountRole.Administrator];

    // xUnit creates the class for every test, so every test has organizations of its own
    private readonly string _run = Guid.NewGuid().ToString("N")[..8];

    private string Alpha => $"iso-inbox-a-{_run}";

    private string Bravo => $"iso-inbox-b-{_run}";

    private IViewInboxStore Store => Context.Services.GetRequiredService<IViewInboxStore>();

    [Fact]
    public async Task Store_KeepsTheItemsOfEachOrganizationApart()
    {
        var now = DateTimeOffset.UtcNow;
        var id = ViewInboxIds.NewId(now);
        var bravoOnly = ViewInboxIds.NewId(now.AddMinutes(1));

        // the same item id in the same partition value of both organizations: an id is unique per full key
        var alpha = await Store.TryCreateAsync(Item(Alpha, id, AlphaMarker, now), Lifetime);
        var bravo = await Store.TryCreateAsync(Item(Bravo, id, BravoMarker, now), Lifetime);
        var bravoSecond = await Store.TryCreateAsync(Item(Bravo, bravoOnly, BravoMarker, now), Lifetime);

        alpha.Should().NotBeNull();
        bravo.Should().NotBeNull("the id is taken in A only");
        bravoSecond.Should().NotBeNull();
        (await OrganizationsOfItemAsync(ViewInboxEntity.GetId(id))).Should().BeEquivalentTo(Alpha, Bravo);

        (await Store.GetAsync(Alpha, Project, View, id))!.Item.Title.Should().Be("alpha item");
        (await Store.GetAsync(Bravo, Project, View, id))!.Item.Title.Should().Be("bravo item");
        (await Store.GetAsync(Alpha, Project, View, bravoOnly)).Should().BeNull();
        (await ListAsync(Alpha, ViewInboxFilter.Open)).Should().Equal((id, "alpha item"));
        (await ListAsync(Bravo, ViewInboxFilter.Open)).Should().Equal((bravoOnly, "bravo item"), (id, "bravo item"));
        (await Store.CountOpenAsync(Alpha, Project, View, Roles)).Should().Be(1);
        (await Store.CountOpenAsync(Bravo, Project, View, Roles)).Should().Be(2);

        // A's item is dismissed; B's item of the same id stays open, unwritten
        var dismissed = await Store.TryReplaceAsync(Dismissed(alpha!), Lifetime);
        dismissed.Should().NotBeNull();

        (await Store.CountOpenAsync(Alpha, Project, View, Roles)).Should().Be(0);
        (await Store.CountOpenAsync(Bravo, Project, View, Roles)).Should().Be(2);
        (await ListAsync(Alpha, ViewInboxFilter.Done)).Should().Equal((id, "alpha item"));
        (await ListAsync(Bravo, ViewInboxFilter.Done)).Should().BeEmpty();
        var bravoAfter = await Store.GetAsync(Bravo, Project, View, id);
        bravoAfter!.ETag.Should().Be(bravo!.ETag);
        bravoAfter.Item.State.Should().Be(ViewInboxItemState.Open);

        // a replace through A never reaches B's items: an item only B has is missing in A, and B's ETag is not the one
        // of A's item under the same id
        (await Store.TryReplaceAsync(Dismissed(bravoSecond!) with { Organization = Alpha }, Lifetime)).Should().BeNull();
        (await Store.TryReplaceAsync(Dismissed(bravo) with { Organization = Alpha }, Lifetime)).Should().BeNull();

        (await Store.GetAsync(Alpha, Project, View, bravoOnly)).Should().BeNull();
        var alphaAfter = await Store.GetAsync(Alpha, Project, View, id);
        alphaAfter!.ETag.Should().Be(dismissed!.ETag);
        alphaAfter.Item.Title.Should().Be("alpha item");
        var bravoSecondAfter = await Store.GetAsync(Bravo, Project, View, bravoOnly);
        bravoSecondAfter!.ETag.Should().Be(bravoSecond!.ETag);
        bravoSecondAfter.Item.State.Should().Be(ViewInboxItemState.Open);
        (await Store.GetAsync(Bravo, Project, View, id))!.ETag.Should().Be(bravo.ETag);
        (await ListAsync(Bravo, ViewInboxFilter.Open)).Should().Equal((bravoOnly, "bravo item"), (id, "bravo item"));
        (await Store.CountOpenAsync(Bravo, Project, View, Roles)).Should().Be(2);
    }

    [Fact]
    public async Task KeyedPush_FindsOnlyTheItemOfItsOrganization()
    {
        var publisher = new ViewInboxPublisher(Store, Options.Create(new ViewInboxOptions()), TimeProvider.System, NullLogger<ViewInboxPublisher>.Instance);
        var completedAt = DateTimeOffset.UtcNow;

        // the same key and time in both organizations: the same id
        var alpha = await publisher.PushAsync(Push(Alpha, AlphaMarker, completedAt));
        var bravo = await publisher.PushAsync(Push(Bravo, BravoMarker, completedAt));

        bravo.Id.Should().Be(alpha.Id);
        bravo.Title.Should().Be("bravo item", "the push of B is not answered with the item of A");

        // the second push of A finds A's item, not B's
        var again = await publisher.PushAsync(Push(Alpha, "changed", completedAt));

        again.Should().BeEquivalentTo(alpha);
        (await ListAsync(Alpha, ViewInboxFilter.Open)).Should().Equal((alpha.Id, "alpha item"));
        (await ListAsync(Bravo, ViewInboxFilter.Open)).Should().Equal((alpha.Id, "bravo item"));
    }

    private static ViewInboxRecord Item(string organization, string id, string marker, DateTimeOffset now)
    {
        return new ViewInboxRecord
        {
            Organization = organization,
            Project = Project,
            View = View,
            Item = new ViewInboxItem
            {
                Id = id,
                Kind = ViewInboxItemKind.Message,
                Topic = "Welcome",
                Title = $"{marker} item",
                MinimalRole = AccountRole.Contributor,
                State = ViewInboxItemState.Open,
                CreatedAt = now,
                CreatedBy = Author(marker),
                ExpiresAt = now + Lifetime,
            },
        };
    }

    private static ViewInboxPush Push(string organization, string marker, DateTimeOffset completedAt)
    {
        return new ViewInboxPush
        {
            Organization = organization,
            Project = Project,
            View = View,
            Kind = ViewInboxItemKind.Notification,
            Topic = ViewInboxTopics.ViewExport,
            Title = $"{marker} item",
            MinimalRole = AccountRole.Administrator,
            CreatedBy = Author(marker),
            Key = "ViewExport.0123456789abcdef0123456789abcdef",
            CreatedAt = completedAt,
        };
    }

    private static ViewInboxRecord Dismissed(ViewInboxRecord record)
    {
        return record with
        {
            Item = record.Item with { State = ViewInboxItemState.Dismissed, Closed = AuditStamp.Of(record.Item.CreatedBy, record.Item.CreatedAt.AddMinutes(5)) },
        };
    }

    private static AuditActor Author(string marker)
    {
        return AuditActor.Account($"user_{marker}", $"{marker} writer");
    }

    // The ids and the titles of the items of the organization, newest first.
    private async Task<IEnumerable<(string Id, string Title)>> ListAsync(string organization, ViewInboxFilter filter)
    {
        var records = await Store.QueryAsync(organization, Project, View, filter, Roles, before: null, take: 10);
        return records.Select(record => (record.Item.Id, record.Item.Title));
    }

    // The organizations of the items with the id, over every partition of the shared container.
    private async Task<IReadOnlyList<string>> OrganizationsOfItemAsync(string id)
    {
        // the container of any organization is the shared one; a query without a partition key spans all of them
        var container = Context.Services.GetRequiredService<IContainerRepositoryProvider>().GetOrganizationContainer(Alpha).Container;
        var query = new QueryDefinition("SELECT VALUE c.Organization FROM c WHERE c.id = @id").WithParameter("@id", id);
        var organizations = new List<string>();

        using var crossOrganization = OrganizationQueryGuard.AllowCrossOrganization();
        using var iterator = container.GetItemQueryIterator<string>(query);

        while (iterator.HasMoreResults)
        {
            organizations.AddRange(await iterator.ReadNextAsync());
        }

        return organizations;
    }
}
