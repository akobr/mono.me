#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Entities.Access;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

using Permission = _42.Platform.Storyteller.Accessing.Model.Permission;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The read of an organization's access audit trail (IAccessAuditLog): both sources merged newest first, every filter, the
// keyset paging across the sources, the isolation between organizations and the continuation token. Every test writes
// the records of an organization of its own through the real services: memberships and access points in core "access",
// shared certificates and trusted issuers in the container organizations. The expected order is the stored records'
// (read straight from their partitions), so it never depends on how the emulator pages a query.
public class CosmosAccessAuditLogTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string MemberName = "Bob";

    private const string IssuerName = "github";

    // A record id of the right shape: aca.{yyyyMMddHHmmssfffffff}.{guid N}.
    private const string SampleId = "aca.202611151200000000000.0123456789abcdef0123456789abcdef";

    private static readonly SharedCertificate Edge = new()
    {
        Thumbprint = "0",
        Label = "edge",
        NotBefore = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        NotAfter = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private static readonly FederatedIssuer GitHub = new()
    {
        Name = IssuerName,
        Issuer = "https://token.actions.githubusercontent.com",
        Description = "CI",
    };

    // A token of another list or of nothing at all, and ids that are not the shape of a record's.
    public static TheoryData<string> BadTokens { get; } = new()
    {
        "garbage!!",
        "a",
        "not a token",
        Base64Url("hello"),
        Base64Url(SampleId),
        Base64Url($"2:{SampleId}"),
        Base64Url($":{SampleId}"),
        Base64Url("1:"),
        Base64Url("1:aca.123"),
        Base64Url("1:apt.acme"),
        Base64Url($"1:{SampleId.ToUpperInvariant()}"),
        Base64Url($"1:{SampleId}0"),
        Base64Url($"1:{SampleId}\n"),
        Base64Url($"1:{SampleId[..^1]}"),
        Base64Url($"1: {SampleId}"),
        Base64Url($"1:{new string('a', 200)}"),
        new ContinuationToken("Execution", "abc").ToString(),
    };

    private IAccessAuditLog Log => Context.Services.GetRequiredService<IAccessAuditLog>();

    private IAccessService Access => Context.Services.GetRequiredService<IAccessService>();

    private ISharedCertificateStore Certificates => Context.Services.GetRequiredService<ISharedCertificateStore>();

    private IFederatedIssuerStore Issuers => Context.Services.GetRequiredService<IFederatedIssuerStore>();

    private IContainerRepositoryProvider Repositories => Context.Services.GetRequiredService<IContainerRepositoryProvider>();

    [Fact]
    public async Task Query_MergesBothSources_NewestFirst()
    {
        var trail = await SeedAsync(NewOrganizationName());
        var stored = await StoredAsync(trail);

        var response = await Log.QueryAsync(trail.Organization, new AccessAuditQuery(), TestContext.Current.CancellationToken);

        stored.Should().HaveCount(10);
        stored.Select(IsCore).Should().Contain(true).And.Contain(false, "the trail has records of both sources");
        response.Items.Select(entry => entry.Id).Should().Equal(stored.Select(record => record.Id));
        response.Items.Select(entry => entry.Id).Should().BeInDescendingOrder(StringComparer.Ordinal);
        response.Items.Select(entry => entry.Time).Should().BeInDescendingOrder();
        response.Count.Should().Be(10);
        response.ContinuationToken.Should().BeNull();

        // the calls in reverse; the two access points of the account's batch in either order
        response.Items.Take(8).Select(entry => (entry.Action, entry.AccessPointKey)).Should().Equal(
            (AccessAuditAction.MemberGranted, trail.Organization),
            (AccessAuditAction.FederatedIssuerCreated, trail.Point2),
            (AccessAuditAction.AccessPointCreated, trail.Point2),
            (AccessAuditAction.SharedCertificateRevoked, trail.Point1),
            (AccessAuditAction.AccessPointUpdated, trail.Point1),
            (AccessAuditAction.FederatedIssuerCreated, trail.Point1),
            (AccessAuditAction.MemberGranted, trail.Point1),
            (AccessAuditAction.SharedCertificateIssued, trail.Point1));
        response.Items.Skip(8).Select(entry => (entry.Action, entry.AccessPointKey)).Should().BeEquivalentTo(new[]
        {
            (AccessAuditAction.AccessPointCreated, trail.Organization),
            (AccessAuditAction.AccessPointCreated, trail.Point1),
        });
    }

    [Fact]
    public async Task Query_ReturnsEveryFieldOfTheRecords()
    {
        var trail = await SeedAsync(NewOrganizationName());
        var stored = (await StoredAsync(trail)).ToDictionary(record => record.Id, StringComparer.Ordinal);

        var items = (await Log.QueryAsync(trail.Organization, new AccessAuditQuery(), TestContext.Current.CancellationToken)).Items;

        foreach (var entry in items)
        {
            entry.Should().BeEquivalentTo(stored[entry.Id].ToEntry(), "an entry is its record");
            entry.Time.Offset.Should().Be(TimeSpan.Zero);
            entry.Time.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
            entry.Id.Should().StartWith($"{AccessAuditEntity.IdPrefix}{entry.Time.UtcDateTime.ToString("yyyyMMddHHmmssfffffff", CultureInfo.InvariantCulture)}.");
        }

        EntryOf(items, AccessAuditAction.MemberGranted, trail.Point1).Should().BeEquivalentTo(
            Expected(AccessAuditAction.MemberGranted, trail.Point1, trail.ViaMcp) with
            {
                Target = AccessAuditTarget.Account(trail.MemberId, MemberName),
                NewValue = "Contributor",
            },
            options => options.Excluding(entry => entry.Id).Excluding(entry => entry.Time));
        EntryOf(items, AccessAuditAction.MemberGranted, trail.Point1).Actor.Via.Should().Be("mcp");
        EntryOf(items, AccessAuditAction.AccessPointCreated, trail.Point1).Should().BeEquivalentTo(
            Expected(AccessAuditAction.AccessPointCreated, trail.Point1, trail.Owner) with
            {
                Target = AccessAuditTarget.Account(trail.OwnerId, "Owner"),
                NewValue = "Owner",
                Detail = "Billing.",
            },
            options => options.Excluding(entry => entry.Id).Excluding(entry => entry.Time));
        EntryOf(items, AccessAuditAction.AccessPointUpdated, trail.Point1).Should().BeEquivalentTo(
            Expected(AccessAuditAction.AccessPointUpdated, trail.Point1, trail.ViaMcp) with
            {
                OldValue = "Billing.",
                NewValue = "Billing and invoices.",
            },
            options => options.Excluding(entry => entry.Id).Excluding(entry => entry.Time));
        EntryOf(items, AccessAuditAction.SharedCertificateIssued, trail.Point1).Should().BeEquivalentTo(
            Expected(AccessAuditAction.SharedCertificateIssued, trail.Point1, trail.ViaMcp) with
            {
                Target = AccessAuditTarget.Certificate(trail.Certificate.Thumbprint, "edge"),
                NewValue = AccessAuditValues.Certificate(trail.Certificate),
            },
            options => options.Excluding(entry => entry.Id).Excluding(entry => entry.Time));
        EntryOf(items, AccessAuditAction.SharedCertificateRevoked, trail.Point1).Should().BeEquivalentTo(
            Expected(AccessAuditAction.SharedCertificateRevoked, trail.Point1, trail.Owner) with
            {
                Target = AccessAuditTarget.Certificate(trail.Certificate.Thumbprint, "edge"),
                OldValue = AccessAuditValues.Certificate(trail.Certificate),
                NewValue = AccessAuditValues.Certificate(trail.Certificate with { IsRevoked = true }),
            },
            options => options.Excluding(entry => entry.Id).Excluding(entry => entry.Time));
        EntryOf(items, AccessAuditAction.FederatedIssuerCreated, trail.Point2).Should().BeEquivalentTo(
            Expected(AccessAuditAction.FederatedIssuerCreated, trail.Point2, trail.ViaMcp) with
            {
                Target = AccessAuditTarget.Issuer(IssuerName),
                NewValue = AccessAuditValues.Issuer(GitHub),
            },
            options => options.Excluding(entry => entry.Id).Excluding(entry => entry.Time));
    }

    [Fact]
    public async Task Project_ReturnsOnlyTheProjectsRecordsOfBothSources()
    {
        var trail = await SeedAsync(NewOrganizationName());
        var stored = await StoredAsync(trail);

        var first = await Log.QueryAsync(trail.Organization, new AccessAuditQuery { Project = trail.Project1 }, TestContext.Current.CancellationToken);
        var second = await Log.QueryAsync(trail.Organization, new AccessAuditQuery { Project = trail.Project2 }, TestContext.Current.CancellationToken);
        var none = await Log.QueryAsync(trail.Organization, new AccessAuditQuery { Project = "no-such-project" }, TestContext.Current.CancellationToken);

        first.Items.Select(entry => entry.Id).Should().Equal(stored.Where(record => record.Project == trail.Project1).Select(record => record.Id));
        first.Items.Should().HaveCount(6).And.OnlyContain(entry => entry.AccessPointKey == trail.Point1, "neither the organization's own records nor another project's");
        first.Items.Select(entry => entry.Action).Should().Contain(AccessAuditAction.MemberGranted).And.Contain(AccessAuditAction.SharedCertificateIssued);
        first.ContinuationToken.Should().BeNull();
        second.Items.Select(entry => entry.Action).Should().Equal(AccessAuditAction.FederatedIssuerCreated, AccessAuditAction.AccessPointCreated);
        second.Items.Should().OnlyContain(entry => entry.AccessPointKey == trail.Point2);
        none.Items.Should().BeEmpty();
        none.ContinuationToken.Should().BeNull();
    }

    [Theory]
    [InlineData(AccessAuditAction.AccessPointCreated, 3)]
    [InlineData(AccessAuditAction.FederatedIssuerCreated, 2)]
    [InlineData(AccessAuditAction.SharedCertificateRevoked, 1)]
    [InlineData(AccessAuditAction.MachineDeleted, 0)]
    public async Task Action_ReturnsOnlyTheRecordsOfTheAction(AccessAuditAction action, int count)
    {
        var trail = await SeedAsync(NewOrganizationName());
        var stored = await StoredAsync(trail);

        var response = await Log.QueryAsync(trail.Organization, new AccessAuditQuery { Action = action }, TestContext.Current.CancellationToken);

        response.Items.Select(entry => entry.Id).Should().Equal(stored.Where(record => record.Action == action).Select(record => record.Id));
        response.Items.Should().HaveCount(count).And.NotContain(entry => entry.Action != action);
        response.Count.Should().Be(count);
    }

    [Fact]
    public async Task Target_ReturnsOnlyTheRecordsOfTheTarget_InBothSources()
    {
        var trail = await SeedAsync(NewOrganizationName());
        var stored = await StoredAsync(trail);

        async Task<IReadOnlyList<AccessAuditEntry>> OfAsync(string target) =>
            (await Log.QueryAsync(trail.Organization, new AccessAuditQuery { Target = target }, TestContext.Current.CancellationToken)).Items;

        var member = await OfAsync(trail.MemberId);
        var owner = await OfAsync(trail.OwnerId);
        var certificate = await OfAsync(trail.Certificate.Thumbprint);
        var issuer = await OfAsync(IssuerName);

        member.Select(entry => entry.Id).Should().Equal(stored.Where(record => record.Target?.Id == trail.MemberId).Select(record => record.Id));
        member.Select(entry => (entry.Action, entry.AccessPointKey)).Should().Equal(
            (AccessAuditAction.MemberGranted, trail.Organization),
            (AccessAuditAction.MemberGranted, trail.Point1));
        owner.Should().HaveCount(3).And.OnlyContain(entry => entry.Action == AccessAuditAction.AccessPointCreated);
        certificate.Select(entry => entry.Action).Should().Equal(AccessAuditAction.SharedCertificateRevoked, AccessAuditAction.SharedCertificateIssued);
        issuer.Select(entry => entry.AccessPointKey).Should().Equal(trail.Point2, trail.Point1);
        (await OfAsync("no-such-target")).Should().BeEmpty();
    }

    [Fact]
    public async Task From_IsInclusive()
    {
        var trail = await SeedAsync(NewOrganizationName());
        var stored = await StoredAsync(trail);
        var revoked = stored.Single(record => record.Action == AccessAuditAction.SharedCertificateRevoked);
        var time = revoked.ToEntry().Time;

        var from = await Log.QueryAsync(trail.Organization, new AccessAuditQuery { From = time }, TestContext.Current.CancellationToken);
        var after = await Log.QueryAsync(trail.Organization, new AccessAuditQuery { From = time.AddTicks(1) }, TestContext.Current.CancellationToken);

        from.Items.Select(entry => entry.Id).Should().Equal(stored.TakeWhile(record => record != revoked).Append(revoked).Select(record => record.Id));
        from.Items.Should().HaveCount(4).And.OnlyContain(entry => entry.Time >= time);
        from.Items[^1].Id.Should().Be(revoked.Id, "the bound is inclusive");
        after.Items.Select(entry => entry.Id).Should().Equal(from.Items.SkipLast(1).Select(entry => entry.Id));
    }

    [Fact]
    public async Task To_IsInclusive()
    {
        var trail = await SeedAsync(NewOrganizationName());
        var stored = await StoredAsync(trail);
        var updated = stored.Single(record => record.Action == AccessAuditAction.AccessPointUpdated);
        var time = updated.ToEntry().Time;

        var to = await Log.QueryAsync(trail.Organization, new AccessAuditQuery { To = time }, TestContext.Current.CancellationToken);
        var before = await Log.QueryAsync(trail.Organization, new AccessAuditQuery { To = time.AddTicks(-1) }, TestContext.Current.CancellationToken);

        to.Items.Select(entry => entry.Id).Should().Equal(stored.SkipWhile(record => record != updated).Select(record => record.Id));
        to.Items.Should().HaveCount(6).And.OnlyContain(entry => entry.Time <= time);
        to.Items[0].Id.Should().Be(updated.Id, "the bound is inclusive");
        before.Items.Select(entry => entry.Id).Should().Equal(to.Items.Skip(1).Select(entry => entry.Id));
    }

    [Fact]
    public async Task FromAndTo_ReturnTheRecordsBetween_AndTheSameTimeOnlyItsRecord()
    {
        var trail = await SeedAsync(NewOrganizationName());
        var stored = await StoredAsync(trail);
        var newest = stored.Single(record => record.Action == AccessAuditAction.AccessPointCreated && record.Project == trail.Project2);
        var oldest = stored.Single(record => record.Action == AccessAuditAction.FederatedIssuerCreated && record.Project == trail.Project1);
        var query = new AccessAuditQuery { From = oldest.ToEntry().Time, To = newest.ToEntry().Time };

        var between = await Log.QueryAsync(trail.Organization, query, TestContext.Current.CancellationToken);
        var exactly = await Log.QueryAsync(trail.Organization, query with { From = newest.ToEntry().Time }, TestContext.Current.CancellationToken);

        between.Items.Select(entry => entry.Action).Should().Equal(
            AccessAuditAction.AccessPointCreated,
            AccessAuditAction.SharedCertificateRevoked,
            AccessAuditAction.AccessPointUpdated,
            AccessAuditAction.FederatedIssuerCreated);
        between.Items[0].Id.Should().Be(newest.Id);
        between.Items[^1].Id.Should().Be(oldest.Id);
        exactly.Items.Select(entry => entry.Id).Should().Equal(newest.Id);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(AccessAuditQuery.MaxPageSize)]
    public async Task Paging_ReturnsEveryRecordOnce_InOrder_AndTheLastPageHasNoToken(int pageSize)
    {
        var trail = await SeedAsync(NewOrganizationName());
        var stored = await StoredAsync(trail);

        var pages = await ReadPagesAsync(trail.Organization, new AccessAuditQuery { PageSize = pageSize });

        pages.SelectMany(page => page.Items).Select(entry => entry.Id).Should().Equal(stored.Select(record => record.Id));
        pages.Should().HaveCount((stored.Count + pageSize - 1) / pageSize);
        pages.Should().AllSatisfy(page => page.Count.Should().Be(page.Items.Count));
        foreach (var page in pages.SkipLast(1))
        {
            page.Items.Should().HaveCount(pageSize);
            CosmosAccessAuditLog.DecodeToken(page.ContinuationToken).Should().Be(page.Items[^1].Id, "the token is the last id of its page");
        }

        pages[^1].Items.Should().NotBeEmpty();
        pages[^1].ContinuationToken.Should().BeNull();

        // a page that ends in one source and a next page that starts in the other: the newest eight records alternate them
        if (pageSize <= 5)
        {
            var sources = stored.ToDictionary(record => record.Id, IsCore, StringComparer.Ordinal);
            pages.Zip(pages.Skip(1), (before, after) => sources[before.Items[^1].Id] != sources[after.Items[0].Id])
                .Should().Contain(true, "a page boundary falls between the sources");
        }
    }

    [Fact]
    public async Task Paging_WithAFilter_ReturnsEveryMatchingRecordOnce()
    {
        var trail = await SeedAsync(NewOrganizationName());
        var stored = await StoredAsync(trail);

        var pages = await ReadPagesAsync(trail.Organization, new AccessAuditQuery { Project = trail.Project1, PageSize = 4 });

        pages.SelectMany(page => page.Items).Select(entry => entry.Id).Should().Equal(stored.Where(record => record.Project == trail.Project1).Select(record => record.Id));
        pages.Select(page => page.Items.Count).Should().Equal(4, 2);
        pages[^1].ContinuationToken.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Paging_APageSizeBelowOne_IsOne(int pageSize)
    {
        var trail = await SeedAsync(NewOrganizationName());
        var stored = await StoredAsync(trail);

        var page = await Log.QueryAsync(trail.Organization, new AccessAuditQuery { PageSize = pageSize }, TestContext.Current.CancellationToken);

        page.Items.Select(entry => entry.Id).Should().Equal(stored[0].Id);
        CosmosAccessAuditLog.DecodeToken(page.ContinuationToken).Should().Be(stored[0].Id);
    }

    [Fact]
    public async Task Paging_RecordsWrittenBetweenPages_ComeOnlyOnANewFirstPage()
    {
        var trail = await SeedAsync(NewOrganizationName());
        var stored = await StoredAsync(trail);
        var query = new AccessAuditQuery { PageSize = 3 };
        var first = await Log.QueryAsync(trail.Organization, query, TestContext.Current.CancellationToken);

        // one record in each source, newer than every record read so far
        var newcomerId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Grant(newcomerId, trail.Point2, AccountRole.Reader), trail.ViaMcp);
        var late = NewCertificate("late");
        await Certificates.StoreAsync(trail.Organization, trail.Project2, late, trail.ViaMcp);

        var rest = await ReadPagesAsync(trail.Organization, query with { ContinuationToken = first.ContinuationToken });
        var fresh = await Log.QueryAsync(trail.Organization, query, TestContext.Current.CancellationToken);

        first.Items.Concat(rest.SelectMany(page => page.Items)).Select(entry => entry.Id)
            .Should().Equal(stored.Select(record => record.Id), "no record repeated, none skipped, none of the new ones");
        fresh.Items.Select(entry => (entry.Action, entry.Target!.Id)).Should().Equal(
            (AccessAuditAction.SharedCertificateIssued, late.Thumbprint),
            (AccessAuditAction.MemberGranted, newcomerId),
            (AccessAuditAction.MemberGranted, trail.MemberId));
        fresh.Items[2].Id.Should().Be(stored[0].Id);
    }

    [Fact]
    public async Task Isolation_AnotherOrganizationsRecords_NeverAppear_AlsoWhenOneNameStartsWithTheOther()
    {
        var name = NewOrganizationName();
        var shorter = await SeedAsync(name);
        var longer = await SeedAsync($"{name}2", shorter.Project1);

        // one more record of the shorter after the longer's, so the ids of both organizations interleave
        var lateId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Grant(lateId, shorter.Point1, AccountRole.Reader), shorter.ViaMcp);

        var ofShorter = await StoredAsync(shorter);
        var ofLonger = await StoredAsync(longer);

        foreach (var (trail, own) in new[] { (shorter, ofShorter), (longer, ofLonger) })
        {
            var all = await ReadPagesAsync(trail.Organization, new AccessAuditQuery { PageSize = 3 });
            var ofProject = await Log.QueryAsync(trail.Organization, new AccessAuditQuery { Project = shorter.Project1 }, TestContext.Current.CancellationToken);
            var ofIssuer = await Log.QueryAsync(trail.Organization, new AccessAuditQuery { Target = IssuerName }, TestContext.Current.CancellationToken);

            all.SelectMany(page => page.Items).Select(entry => entry.Id).Should().Equal(own.Select(record => record.Id));
            all.SelectMany(page => page.Items).Should().OnlyContain(entry => entry.AccessPointKey.Split('.', 2)[0] == trail.Organization);
            ofProject.Items.Should().NotBeEmpty().And.OnlyContain(entry => entry.AccessPointKey == $"{trail.Organization}.{shorter.Project1}");
            ofIssuer.Items.Should().HaveCount(2).And.OnlyContain(entry => entry.AccessPointKey.StartsWith($"{trail.Organization}.", StringComparison.Ordinal));
        }

        ofShorter.Should().HaveCount(11);
        ofLonger.Should().HaveCount(10);
        ofShorter.Select(record => record.Id).Should().NotIntersectWith(ofLonger.Select(record => record.Id));
    }

    [Fact]
    public async Task TheDocumentsOfAViewNamedAca_AreNoRecords()
    {
        var trail = await SeedAsync(NewOrganizationName());
        var stored = await StoredAsync(trail);
        var repository = Repositories.GetOrganizationContainer(trail.Organization);

        // an annotation and a configuration of the view "aca": their ids start with the prefix of the records
        foreach (var (id, partition) in new[] { ("aca.sbt.web", $"{trail.Project1}.sbt.web"), ("aca.cnf.sbt.web", $"{trail.Project1}.sbt.web") })
        {
            await repository.Container.UpsertItemAsync(
                new { id, Organization = trail.Organization, PartitionKey = partition, ViewName = "aca", ProjectName = trail.Project1 },
                repository.Key(partition),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        var response = await Log.QueryAsync(trail.Organization, new AccessAuditQuery(), TestContext.Current.CancellationToken);
        var ofProject = await Log.QueryAsync(trail.Organization, new AccessAuditQuery { Project = trail.Project1 }, TestContext.Current.CancellationToken);

        response.Items.Select(entry => entry.Id).Should().Equal(stored.Select(record => record.Id));
        ofProject.Items.Select(entry => entry.Id).Should().NotContain(["aca.sbt.web", "aca.cnf.sbt.web"]);
    }

    [Fact]
    public async Task AnOrganizationWithoutRecords_AnswersAnEmptyPageWithoutAToken()
    {
        var organization = NewOrganizationName();

        var first = await Log.QueryAsync(organization, new AccessAuditQuery(), TestContext.Current.CancellationToken);
        var next = await Log.QueryAsync(organization, new AccessAuditQuery { ContinuationToken = CosmosAccessAuditLog.EncodeToken(SampleId) }, TestContext.Current.CancellationToken);

        first.Items.Should().BeEmpty();
        first.Count.Should().Be(0);
        first.ContinuationToken.Should().BeNull();
        next.Items.Should().BeEmpty();
        next.ContinuationToken.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(BadTokens))]
    public async Task ABadToken_IsAnInvalidQuery(string token)
    {
        var query = () => Log.QueryAsync(NewOrganizationName(), new AccessAuditQuery { ContinuationToken = token }, TestContext.Current.CancellationToken);
        var decode = () => CosmosAccessAuditLog.DecodeToken(token);

        (await query.Should().ThrowAsync<InvalidInputException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidQuery);
        decode.Should().Throw<InvalidInputException>().Which.ErrorCode.Should().Be(ErrorCodes.InvalidQuery);
    }

    [Fact]
    public void TheToken_IsTheBase64UrlOfItsVersionAndTheLastId_AndRoundTrips()
    {
        var id = AccessAuditEntity.NewId(DateTimeOffset.UtcNow);

        var token = CosmosAccessAuditLog.EncodeToken(id);

        token.Should().Be(Base64Url($"1:{id}")).And.MatchRegex("^[A-Za-z0-9_-]+$");
        CosmosAccessAuditLog.DecodeToken(token).Should().Be(id);
        CosmosAccessAuditLog.DecodeToken($"  {token} ").Should().Be(id);
        CosmosAccessAuditLog.DecodeToken(Convert.ToBase64String(Encoding.UTF8.GetBytes($"1:{id}"))).Should().Be(id, "the padded form decodes too");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoToken_IsTheFirstPage(string? token)
    {
        CosmosAccessAuditLog.DecodeToken(token).Should().BeNull();
    }

    private static string Base64Url(string text)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string NewOrganizationName() => $"l-{Guid.NewGuid():N}"[..14];

    private static string NewName(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..14];

    private static SharedCertificate NewCertificate(string label) => Edge with { Thumbprint = Guid.NewGuid().ToString("N").ToUpperInvariant(), Label = label };

    private static bool IsCore(AccessAuditEntity record) => record.PartitionKey == AccessPointEntity.Partition;

    // CreatedById is not the caller: the service authorizes the caller.
    private static Permission Grant(string accountId, string pointKey, AccountRole role) => new()
    {
        CreatedById = "not-the-caller",
        AccountId = accountId,
        AccessPointKey = pointKey,
        Role = role,
    };

    private static AccessAuditEntry Expected(AccessAuditAction action, string accessPointKey, AccessCaller caller) => new()
    {
        Id = string.Empty,
        Time = default,
        Action = action,
        Actor = caller.Actor,
        AccessPointKey = accessPointKey,
        CorrelationId = caller.CorrelationId,
    };

    private static AccessAuditEntry EntryOf(IEnumerable<AccessAuditEntry> items, AccessAuditAction action, string accessPointKey)
    {
        return items.Should().ContainSingle(entry => entry.Action == action && entry.AccessPointKey == accessPointKey).Subject;
    }

    // Every page of the query from its token on.
    private async Task<IReadOnlyList<AccessAuditResponse>> ReadPagesAsync(string organization, AccessAuditQuery query)
    {
        var pages = new List<AccessAuditResponse>();
        var token = query.ContinuationToken;

        do
        {
            var page = await Log.QueryAsync(organization, query with { ContinuationToken = token }, TestContext.Current.CancellationToken);
            pages.Add(page);
            token = page.ContinuationToken;
            pages.Should().HaveCountLessThan(100, "the paging ends");
        }
        while (token is not null);

        return pages;
    }

    // Ten records of an organization of its own, one call after another, alternating the sources (newest first: core,
    // organizations, core, …, and the two access points of the account's batch last):
    // core "access": the organization and its first project created with the owner's account (one batch), a member
    // granted the first project, its description changed, the second project created, the member granted the organization;
    // organizations: a shared certificate issued and revoked in the first project, the trusted issuer of both projects.
    private async Task<Trail> SeedAsync(string organization, string? project1 = null)
    {
        var ownerId = $"own-{Guid.NewGuid():N}";
        var correlation = Guid.NewGuid().ToString("N")[..8];
        var trail = new Trail(
            organization,
            project1 ?? NewName("p"),
            NewName("q"),
            ownerId,
            await CreateBareAccountAsync(),
            NewCertificate("edge"),
            AccessCaller.Account(ownerId, "Owner") with { CorrelationId = $"corr-owner-{correlation}" },
            new AccessCaller(AuditActor.Account(ownerId, "Ada") with { Via = "mcp" }) { CorrelationId = $"corr-mcp-{correlation}" });

        await Access.CreateAccountAsync(
            new AccountCreate
            {
                IdentityId = ownerId,
                UserName = $"{ownerId}@example.com",
                Name = "Owner",
                Organization = organization,
                Project = trail.Project1,
                ProjectDescription = "Billing.",
            },
            trail.Owner);
        await Certificates.StoreAsync(organization, trail.Project1, trail.Certificate, trail.ViaMcp);
        await Access.GrantPermissionAsync(Grant(trail.MemberId, trail.Point1, AccountRole.Contributor), trail.ViaMcp);
        await Issuers.SetAsync(organization, trail.Project1, GitHub, trail.ViaMcp, TestContext.Current.CancellationToken);
        await Access.UpdateAccessPointAsync(trail.Point1, new AccessPointUpdate { Description = "Billing and invoices." }, trail.ViaMcp);
        (await Certificates.RevokeAsync(organization, trail.Project1, trail.Certificate.Thumbprint, trail.Owner)).Should().BeTrue();
        await Access.CreateAccessPointAsync(new AccessPointCreate { Organization = organization, Project = trail.Project2, OwnerId = ownerId }, trail.Owner);
        await Issuers.SetAsync(organization, trail.Project2, GitHub, trail.ViaMcp, TestContext.Current.CancellationToken);
        await Access.GrantPermissionAsync(Grant(trail.MemberId, organization, AccountRole.Reader), trail.ViaMcp);

        return trail;
    }

    // The records of the trail as stored, read straight from every partition they can be in, newest first.
    private async Task<IReadOnlyList<AccessAuditEntity>> StoredAsync(Trail trail)
    {
        var records = new List<AccessAuditEntity>();

        foreach (var key in new[] { trail.Organization, trail.Point1, trail.Point2 })
        {
            records.AddRange(await StoredAccessAudit.ReadCoreAsync(Repositories, key));
        }

        foreach (var project in new[] { trail.Project1, trail.Project2 })
        {
            records.AddRange(await StoredAccessAudit.ReadAsync(Repositories, trail.Organization, $"{project}.access"));
            records.AddRange(await StoredAccessAudit.ReadAsync(Repositories, trail.Organization, CosmosFederatedIssuerStore.GetPartition(project)));
        }

        return records.OrderByDescending(record => record.Id, StringComparer.Ordinal).ToList();
    }

    // An account without memberships, created directly: CreateAccountAsync with a project always creates an organization.
    private async Task<string> CreateBareAccountAsync()
    {
        var id = $"acc-{Guid.NewGuid():N}";
        await Repositories.GetCore().Container.CreateItemAsync(
            new AccountEntity { Id = id, UserName = $"{id}@example.com", Name = MemberName, AccessMap = new() },
            new PartitionKey(AccessPointEntity.Partition),
            cancellationToken: TestContext.Current.CancellationToken);
        return id;
    }

    private sealed record Trail(
        string Organization,
        string Project1,
        string Project2,
        string OwnerId,
        string MemberId,
        SharedCertificate Certificate,
        AccessCaller Owner,
        AccessCaller ViaMcp)
    {
        public string Point1 => $"{Organization}.{Project1}";

        public string Point2 => $"{Organization}.{Project2}";
    }
}
