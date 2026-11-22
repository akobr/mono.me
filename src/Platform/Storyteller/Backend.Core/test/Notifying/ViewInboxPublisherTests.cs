using _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;
using _42.Platform.Storyteller.Notifying;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static _42.Platform.Storyteller.Backend.Core.UnitTests.Notifying.ViewInboxTestData;
using static _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring.TransferTestData;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Notifying;

public class ViewInboxPublisherTests
{
    private readonly ManualTimeProvider _time = new(Start);
    private readonly InMemoryViewInboxStore _store = new();
    private readonly ViewInboxOptions _options = new();
    private readonly ViewInboxPublisher _publisher;

    public ViewInboxPublisherTests()
    {
        _publisher = new ViewInboxPublisher(_store, Options.Create(_options), _time, NullLogger<ViewInboxPublisher>.Instance);
    }

    [Fact]
    public async Task Push_StoresAnOpenItemInTheView()
    {
        var job = new ViewInboxJob { Kind = ViewInboxJobKind.ViewExport, Id = "0123", FileName = "export.zip" };

        var item = await _publisher.PushAsync(Push() with { Level = ViewInboxItemLevel.Success, Text = "12 annotations, 1.2 MB", CreatedBy = Actor, Job = job });

        ViewInboxIds.IsValid(item.Id).ShouldBeTrue();
        item.Id.ShouldStartWith("202610010800000000000");
        item.ShouldBe(new ViewInboxItem
        {
            Id = item.Id,
            Kind = ViewInboxItemKind.Notification,
            Topic = "ViewExport",
            Level = ViewInboxItemLevel.Success,
            Title = "Export finished",
            Text = "12 annotations, 1.2 MB",
            MinimalRole = AccountRole.Administrator,
            State = ViewInboxItemState.Open,
            CreatedAt = Start,
            CreatedBy = Actor,
            ExpiresAt = Start.AddDays(30),
            Job = job,
        });

        var stored = _store.Writes.ShouldHaveSingleItem().Record;
        (stored.Organization, stored.Project, stored.View).ShouldBe(("acme", "main", "default"));
        stored.Item.ShouldBe(item);
        stored.ETag.ShouldNotBeNull();
    }

    [Fact]
    public async Task Push_StoresAnActionWithTheCodeOfItsHandler()
    {
        var item = await _publisher.PushAsync(Push(ViewInboxItemKind.Action, AccountRole.Contributor));

        item.Kind.ShouldBe(ViewInboxItemKind.Action);
        item.Action.ShouldBe(HandledAction);
        item.Topic.ShouldBe("SchemaSuggestion");
        item.MinimalRole.ShouldBe(AccountRole.Contributor);
        item.Level.ShouldBe(ViewInboxItemLevel.Info);
        item.CreatedBy.ShouldBe(AuditActor.System);
        item.Closed.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Push_RefusesABlankOrganizationProjectViewOrTitle(string blank)
    {
        await ShouldBeRefusedAsync(Push() with { Organization = blank });
        await ShouldBeRefusedAsync(Push() with { Project = blank });
        await ShouldBeRefusedAsync(Push() with { View = blank });
        await ShouldBeRefusedAsync(Push() with { Title = blank });
    }

    [Theory]
    [InlineData("")]
    [InlineData("1Export")]
    [InlineData("View Export")]
    [InlineData("view-export")]
    [InlineData("view.export")]
    [InlineData("view_export")]
    [InlineData("Náhled")]
    [InlineData("ViewExport\n")]
    public async Task Push_RefusesATopicOrAnActionThatIsNotACode(string code)
    {
        await ShouldBeRefusedAsync(Push() with { Topic = code });
        await ShouldBeRefusedAsync(Push(ViewInboxItemKind.Action) with { Topic = code });
        await ShouldBeRefusedAsync(Push(ViewInboxItemKind.Action) with { Action = code });
    }

    [Fact]
    public async Task Push_TakesACodeOfALetterAndUpToSixtyThreeLettersOrDigits()
    {
        var longest = "A" + new string('9', 63);

        (await _publisher.PushAsync(Push() with { Topic = "a" })).Topic.ShouldBe("a");

        var action = await _publisher.PushAsync(Push(ViewInboxItemKind.Action) with { Topic = longest, Action = longest });
        action.Topic.ShouldBe(longest);
        action.Action.ShouldBe(longest);

        await ShouldBeRefusedAsync(Push() with { Topic = longest + "9" });
        await ShouldBeRefusedAsync(Push(ViewInboxItemKind.Action) with { Action = longest + "9" });
    }

    [Fact]
    public async Task Push_RefusesAnActionWithoutACode_AndACodeOnAnotherKind()
    {
        await ShouldBeRefusedAsync(Push(ViewInboxItemKind.Action) with { Action = null });
        await ShouldBeRefusedAsync(Push(ViewInboxItemKind.Notification) with { Action = HandledAction });
        await ShouldBeRefusedAsync(Push(ViewInboxItemKind.Message) with { Action = HandledAction });
    }

    [Theory]
    [InlineData(AccountRole.None)]
    [InlineData(AccountRole.Reader)]
    public async Task Push_RefusesAMinimalRoleBelowContributor(AccountRole role)
    {
        await ShouldBeRefusedAsync(Push(role: role));
    }

    [Theory]
    [InlineData(AccountRole.Contributor)]
    [InlineData(AccountRole.ContributorWithSecrets)]
    [InlineData(AccountRole.Administrator)]
    [InlineData(AccountRole.Owner)]
    public async Task Push_TakesAMinimalRoleFromContributorUp(AccountRole role)
    {
        (await _publisher.PushAsync(Push(role: role))).MinimalRole.ShouldBe(role);
    }

    [Fact]
    public async Task Push_RefusesAKeyWithoutTheTimeOfItsEvent()
    {
        await ShouldBeRefusedAsync(Push() with { Key = "ViewExport.0123" });
    }

    [Fact]
    public async Task Push_CutsALongTitleAndALongText()
    {
        var cut = await _publisher.PushAsync(Push() with { Title = new string('t', 201), Text = new string('x', 2001) });

        cut.Title.ShouldBe(new string('t', 199) + "…");
        cut.Text.ShouldBe(new string('x', 1999) + "…");

        var whole = await _publisher.PushAsync(Push() with { Title = new string('t', 200), Text = new string('x', 2000) });

        whole.Title.ShouldBe(new string('t', 200));
        whole.Text.ShouldBe(new string('x', 2000));
    }

    [Fact]
    public async Task Push_WithoutAnExpiry_LivesForTheRetentionOfTheOptions()
    {
        (await _publisher.PushAsync(Push())).ExpiresAt.ShouldBe(Start.AddDays(30));

        _options.RetentionDays = 7;
        _time.Now = Start.AddHours(1);

        (await _publisher.PushAsync(Push())).ExpiresAt.ShouldBe(Start.AddHours(1).AddDays(7));
    }

    [Fact]
    public async Task Push_KeepsAnExpiryBetweenAMinuteAndAYearFromNow()
    {
        (await ExpiryOfAsync(Start.AddMinutes(1))).ShouldBe(Start.AddMinutes(1));
        (await ExpiryOfAsync(Start.AddHours(24))).ShouldBe(Start.AddHours(24));
        (await ExpiryOfAsync(Start.AddDays(365))).ShouldBe(Start.AddDays(365));
    }

    [Fact]
    public async Task Push_MovesAnExpiryOutsideTheLimitsToTheLimit()
    {
        (await ExpiryOfAsync(Start.AddSeconds(59))).ShouldBe(Start.AddMinutes(1));
        (await ExpiryOfAsync(Start)).ShouldBe(Start.AddMinutes(1));
        (await ExpiryOfAsync(Start.AddDays(-3))).ShouldBe(Start.AddMinutes(1));
        (await ExpiryOfAsync(DateTimeOffset.MinValue)).ShouldBe(Start.AddMinutes(1));

        (await ExpiryOfAsync(Start.AddDays(365).AddSeconds(1))).ShouldBe(Start.AddDays(365));
        (await ExpiryOfAsync(DateTimeOffset.MaxValue)).ShouldBe(Start.AddDays(365));
    }

    [Fact]
    public async Task Push_GivesTheWriteTheTimeTheItemStillHas()
    {
        await _publisher.PushAsync(Push());
        await _publisher.PushAsync(Push() with { ExpiresAt = Start.AddHours(2) });
        await _publisher.PushAsync(Push() with { ExpiresAt = Start.AddDays(400) });
        await _publisher.PushAsync(Push() with { ExpiresAt = Start.AddSeconds(1) });

        // an event of the past lives from now on, not from its time
        await _publisher.PushAsync(Push() with { Key = "ViewExport.0123", CreatedAt = Start.AddDays(-2) });

        _store.Writes.Select(write => write.TimeToLive).ShouldBe(
        [
            TimeSpan.FromDays(30),
            TimeSpan.FromHours(2),
            TimeSpan.FromDays(365),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromDays(30),
        ]);
    }

    [Fact]
    public async Task Push_WithoutAKey_StoresANewItemEachTime()
    {
        var first = await _publisher.PushAsync(Push());
        var second = await _publisher.PushAsync(Push());

        second.Id.ShouldNotBe(first.Id);
        _store.Writes.Count.ShouldBe(2);

        // the time of the event, when the producer knows it, is the time of the item and the start of its id
        var past = await _publisher.PushAsync(Push() with { CreatedAt = Start.AddHours(-3) });

        past.CreatedAt.ShouldBe(Start.AddHours(-3));
        past.Id.ShouldStartWith("202610010500000000000");
        past.ExpiresAt.ShouldBe(Start.AddDays(30));
    }

    [Fact]
    public async Task Push_WithoutAKey_NeverAnswersWithTheItemOfATakenId()
    {
        // another writer stores the very id first
        _store.Creating = record =>
        {
            _store.Creating = null;
            _ = _store.TryCreateAsync(record with { Item = record.Item with { Title = "Of another writer" } }, TimeSpan.FromHours(1));
        };

        await Should.ThrowAsync<InvalidOperationException>(() => _publisher.PushAsync(Push()));

        _store.Writes.ShouldHaveSingleItem().Record.Item.Title.ShouldBe("Of another writer");
    }

    [Fact]
    public async Task Push_WithAKey_GivesTheSameItemForTheSameKeyAndTime()
    {
        var completedAt = Start.AddMinutes(-5);
        var push = Push() with { Key = "ViewExport.0123", CreatedAt = completedAt };

        var first = await _publisher.PushAsync(push);

        first.Id.ShouldBe(ViewInboxIds.ForKey(completedAt, "ViewExport.0123"));
        first.CreatedAt.ShouldBe(completedAt);
        first.ExpiresAt.ShouldBe(Start.AddDays(30));

        // the same event again, later, read back with another offset and described in other words
        _time.Now = Start.AddMinutes(3);
        var second = await _publisher.PushAsync(push with { CreatedAt = completedAt.ToOffset(TimeSpan.FromHours(2)), Title = "Export failed", Level = ViewInboxItemLevel.Error });

        second.ShouldBe(first);
        _store.Writes.Count.ShouldBe(1);
        _store.Stored(first.Id).ShouldBe(first);
    }

    [Fact]
    public async Task Push_WithAKey_GivesAnotherItemForAnotherKeyOrAnotherTime()
    {
        var push = Push() with { Key = "ViewExport.0123", CreatedAt = Start };

        var first = await _publisher.PushAsync(push);
        var otherKey = await _publisher.PushAsync(push with { Key = "ViewImport.0123" });
        var otherTime = await _publisher.PushAsync(push with { CreatedAt = Start.AddTicks(1) });

        new[] { first.Id, otherKey.Id, otherTime.Id }.Distinct().Count().ShouldBe(3);
        _store.Writes.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData(ViewInboxItemState.Accepted)]
    [InlineData(ViewInboxItemState.Dismissed)]
    public async Task Push_WithAKey_ReturnsAnItemClosedMeanwhileAsItIs(ViewInboxItemState state)
    {
        var push = Push() with { Key = "ViewExport.0123", CreatedAt = Start };
        var first = await _publisher.PushAsync(push);
        var closed = first with { State = state, Closed = AuditStamp.Of(Other, Start.AddMinutes(1)) };
        _store.TakeOver(first.Id, _ => closed);

        var second = await _publisher.PushAsync(push);

        second.ShouldBe(closed);
        _store.Stored(first.Id).ShouldBe(closed);
        _store.Writes.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Push_WithAKey_CreatesTheItemAgain_WhenTheStoredOneExpiresBeforeItIsRead()
    {
        var push = Push() with { Key = "ViewExport.0123", CreatedAt = Start, ExpiresAt = Start.AddHours(1) };
        var first = await _publisher.PushAsync(push);
        _store.TakeOver(first.Id, item => item with { State = ViewInboxItemState.Dismissed });

        // the create of the second push meets the stored item, which is gone ten minutes later, when it is read
        _store.Reading = id =>
        {
            _store.Remove(id);
            _time.Now = Start.AddMinutes(10);
        };

        var second = await _publisher.PushAsync(push);

        second.ShouldBe(first);
        _store.Stored(first.Id).ShouldBe(second);
        _store.Reads.ShouldBe([first.Id]);
        _store.Writes.Count.ShouldBe(2);

        // the new item ends when the push said, not an hour after it was written
        _store.Writes[^1].TimeToLive.ShouldBe(TimeSpan.FromMinutes(50));
    }

    [Fact]
    public async Task Push_WithAKey_Throws_WhenTheCreateConflictsAgain()
    {
        var push = Push() with { Key = "ViewExport.0123", CreatedAt = Start };
        var first = await _publisher.PushAsync(push);
        _store.Hide(first.Id);

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => _publisher.PushAsync(push));

        exception.Message.ShouldContain(first.Id);
        _store.Reads.ShouldBe([first.Id]);
        _store.Writes.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Push_StopsWithTheTokenOfItsCaller()
    {
        await Should.ThrowAsync<OperationCanceledException>(() => _publisher.PushAsync(Push(), new CancellationToken(canceled: true)));

        _store.Writes.ShouldBeEmpty();
    }

    // The expiry of an item pushed now with the given one.
    private async Task<DateTimeOffset> ExpiryOfAsync(DateTimeOffset expiresAt)
    {
        return (await _publisher.PushAsync(Push() with { ExpiresAt = expiresAt })).ExpiresAt;
    }

    // A wrong push is an error of its producer, and nothing is stored.
    private async Task ShouldBeRefusedAsync(ViewInboxPush push)
    {
        var writes = _store.Writes.Count;

        await Should.ThrowAsync<ArgumentException>(() => _publisher.PushAsync(push));

        _store.Writes.Count.ShouldBe(writes);
    }
}
