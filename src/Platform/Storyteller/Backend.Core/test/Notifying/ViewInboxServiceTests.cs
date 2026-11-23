using _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;
using _42.Platform.Storyteller.Notifying;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static _42.Platform.Storyteller.Backend.Core.UnitTests.Notifying.ViewInboxTestData;
using static _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring.TransferTestData;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Notifying;

public class ViewInboxServiceTests
{
    private static readonly AccountRole[] ItemRoles =
    [
        AccountRole.Contributor,
        AccountRole.ContributorWithSecrets,
        AccountRole.Administrator,
        AccountRole.Owner,
    ];

    private readonly ManualTimeProvider _time = new(Start);
    private readonly InMemoryViewInboxStore _store = new();
    private readonly RecordingActionHandler _handler = new(HandledAction);
    private readonly CapturingLogger<ViewInboxService> _logger = new();
    private readonly ViewInboxPublisher _publisher;
    private readonly ViewInboxService _service;

    public ViewInboxServiceTests()
    {
        _publisher = new ViewInboxPublisher(_store, Options.Create(new ViewInboxOptions()), _time, NullLogger<ViewInboxPublisher>.Instance);
        _service = new ViewInboxService(_store, [_handler], _time, _logger);
    }

    public static TheoryData<string> ForeignTokens => new()
    {
        "x",
        "not a token!",
        Token(UnknownId),
        Token($"2:{UnknownId}"),
        Token($"1:{UnknownId}0"),
        Token($"1:{UnknownId[..^1]}"),
        Token($"1:{UnknownId.ToUpperInvariant()}"),
        Token($"1:{UnknownId}\n"),
        Token("1:"),
        Token("1:aca.202610010800000000000.00112233445566778899aabbccddeeff"),
    };

    [Theory]
    [InlineData(AccountRole.None, 0)]
    [InlineData(AccountRole.Reader, 0)]
    [InlineData(AccountRole.Contributor, 1)]
    [InlineData(AccountRole.ContributorWithSecrets, 2)]
    [InlineData(AccountRole.Administrator, 3)]
    [InlineData(AccountRole.Owner, 4)]
    public async Task GetItems_ShowsTheItemsBetweenContributorAndTheRoleOfTheCaller(AccountRole role, int expected)
    {
        var items = new List<ViewInboxItem>();

        foreach (var minimalRole in ItemRoles)
        {
            items.AddRange(await PushManyAsync(1, role: minimalRole));
        }

        var open = await ListAsync(role);

        open.Items.Select(item => item.MinimalRole).ShouldBe(ItemRoles.Take(expected).Reverse());
        open.Count.ShouldBe(expected);
        open.OpenCount.ShouldBe(expected);
        open.ContinuationToken.ShouldBeNull();

        // the closed items follow the same rule
        items.ForEach(item => _store.TakeOver(item.Id, stored => stored with { State = ViewInboxItemState.Dismissed }));
        var done = await ListAsync(role, ViewInboxFilter.Done);

        done.Items.Select(item => item.MinimalRole).ShouldBe(ItemRoles.Take(expected).Reverse());
        done.Count.ShouldBe(expected);
        done.OpenCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(ViewInboxItemKind.Notification, ViewInboxItemState.Open)]
    [InlineData(ViewInboxItemKind.Action, ViewInboxItemState.Open)]
    [InlineData(ViewInboxItemKind.Action, ViewInboxItemState.Accepted)]
    [InlineData(ViewInboxItemKind.Action, ViewInboxItemState.Dismissed)]
    [InlineData(ViewInboxItemKind.Message, ViewInboxItemState.Dismissed)]
    public async Task AcceptAndDismiss_OfAnItemAboveTheRole_AreNotFound_WhateverItsKindAndState(ViewInboxItemKind kind, ViewInboxItemState state)
    {
        var item = await PushAsync(kind, AccountRole.Administrator);
        var stored = item with { State = state, Closed = state == ViewInboxItemState.Open ? null : AuditStamp.Of(Other, Start) };
        _store.TakeOver(item.Id, _ => stored);
        var writes = _store.Writes.Count;

        foreach (var role in new[] { AccountRole.Contributor, AccountRole.ContributorWithSecrets })
        {
            // never a conflict, which would tell that the item exists and what it is
            var accept = await Should.ThrowAsync<NotFoundException>(() => AcceptAsync(item.Id, role));
            var dismiss = await Should.ThrowAsync<NotFoundException>(() => DismissAsync(item.Id, role));

            accept.Message.ShouldBe($"The inbox item '{item.Id}' does not exist or has expired.");
            accept.ErrorCode.ShouldBe(ErrorCodes.NotFound);
            dismiss.Message.ShouldBe(accept.Message);
            dismiss.ErrorCode.ShouldBe(ErrorCodes.NotFound);
        }

        _store.Stored(item.Id).ShouldBe(stored);
        _store.Writes.Count.ShouldBe(writes);
        _handler.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task AcceptAndDismiss_OfAnOwnersItem_AreNotFoundForAnAdministrator()
    {
        var action = await PushAsync(ViewInboxItemKind.Action, AccountRole.Owner);
        var message = await PushAsync(ViewInboxItemKind.Message, AccountRole.Owner);

        await Should.ThrowAsync<NotFoundException>(() => AcceptAsync(action.Id, AccountRole.Administrator));
        await Should.ThrowAsync<NotFoundException>(() => DismissAsync(message.Id, AccountRole.Administrator));

        (await AcceptAsync(action.Id, AccountRole.Owner)).State.ShouldBe(ViewInboxItemState.Accepted);
        (await DismissAsync(message.Id, AccountRole.Owner)).State.ShouldBe(ViewInboxItemState.Dismissed);
    }

    [Fact]
    public async Task AcceptAndDismiss_OfAnUnknownId_AreNotFound()
    {
        await PushAsync();

        var accept = await Should.ThrowAsync<NotFoundException>(() => AcceptAsync(UnknownId, AccountRole.Owner));
        var dismiss = await Should.ThrowAsync<NotFoundException>(() => DismissAsync(UnknownId, AccountRole.Owner));

        accept.Message.ShouldBe($"The inbox item '{UnknownId}' does not exist or has expired.");
        dismiss.Message.ShouldBe(accept.Message);
        _store.Reads.ShouldBe([UnknownId, UnknownId]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData(UnknownId + "0")]
    [InlineData("inb." + UnknownId)]
    [InlineData("../" + UnknownId)]
    [InlineData("202610010800000000000" + "00112233445566778899AABBCCDDEEFF")]
    public async Task AcceptAndDismiss_OfAnIdOfAnotherShape_NeverReachTheStore(string id)
    {
        var accept = await Should.ThrowAsync<NotFoundException>(() => AcceptAsync(id, AccountRole.Owner));
        var dismiss = await Should.ThrowAsync<NotFoundException>(() => DismissAsync(id, AccountRole.Owner));

        accept.Message.ShouldBe($"The inbox item '{id}' does not exist or has expired.");
        dismiss.Message.ShouldBe(accept.Message);
        _store.Reads.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheInbox_FindsAnItemOnlyThroughItsView()
    {
        var item = await PushAsync();

        await Should.ThrowAsync<NotFoundException>(() => _service.DismissAsync("acme", "main", "other", item.Id, AccountRole.Owner, Actor));
        await Should.ThrowAsync<NotFoundException>(() => _service.DismissAsync("acme", "other", "default", item.Id, AccountRole.Owner, Actor));
        await Should.ThrowAsync<NotFoundException>(() => _service.AcceptAsync("other", "main", "default", item.Id, AccountRole.Owner, Actor));

        var other = await _service.GetItemsAsync("acme", "main", "other", new ViewInboxQuery { Role = AccountRole.Owner });
        other.Items.ShouldBeEmpty();
        other.OpenCount.ShouldBe(0);

        _store.Stored(item.Id).ShouldBe(item);
        (await ListAsync(AccountRole.Owner)).Items.ShouldBe([item]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AcceptAndDismiss_OfAnExpiredItem_AreNotFound_BeforeTheStoreRemovesIt(bool accept)
    {
        var item = await _publisher.PushAsync(Push(ViewInboxItemKind.Action) with { ExpiresAt = Start.AddHours(1) });

        _time.Now = Start.AddHours(1);
        var exception = await Should.ThrowAsync<NotFoundException>(() => CloseAsync(accept, item.Id));

        exception.Message.ShouldBe($"The inbox item '{item.Id}' does not exist or has expired.");
        _store.Stored(item.Id).ShouldBe(item);
        _handler.Runs.ShouldBeEmpty();

        // a tick before its end the item is still there
        _time.Now = Start.AddHours(1).AddTicks(-1);
        var closed = await CloseAsync(accept, item.Id);

        closed.State.ShouldBe(accept ? ViewInboxItemState.Accepted : ViewInboxItemState.Dismissed);
    }

    [Fact]
    public async Task GetItems_ReturnsTheNewestFirst()
    {
        var pushed = await PushManyAsync(3);

        var page = await ListAsync(AccountRole.Administrator);

        page.Items.ShouldBe([pushed[2], pushed[1], pushed[0]]);
        page.Count.ShouldBe(3);
        page.ContinuationToken.ShouldBeNull();
    }

    [Fact]
    public async Task GetItems_PagesByTheIdOfTheLastItem()
    {
        var pushed = await PushManyAsync(120);

        var first = await ListAsync(AccountRole.Administrator);
        var second = await ListAsync(AccountRole.Administrator, token: first.ContinuationToken);
        var third = await ListAsync(AccountRole.Administrator, token: second.ContinuationToken);

        (first.Items.Count, second.Items.Count, third.Items.Count).ShouldBe((50, 50, 20));
        (first.Count, second.Count, third.Count).ShouldBe((50, 50, 20));
        first.Items.Concat(second.Items).Concat(third.Items).ShouldBe(Enumerable.Reverse(pushed));

        first.ContinuationToken.ShouldBe(Token($"1:{first.Items[^1].Id}"));
        second.ContinuationToken.ShouldBe(Token($"1:{second.Items[^1].Id}"));
        third.ContinuationToken.ShouldBeNull();

        // one more than a page is read, to know whether a page follows
        _store.Queries.Select(query => (query.Before, query.Take)).ShouldBe(
        [
            ((string?)null, 51),
            (first.Items[^1].Id, 51),
            (second.Items[^1].Id, 51),
        ]);
    }

    [Fact]
    public async Task GetItems_OfExactlyOnePage_HasNoToken()
    {
        var pushed = await PushManyAsync(50);

        var page = await ListAsync(AccountRole.Administrator);

        page.Count.ShouldBe(50);
        page.ContinuationToken.ShouldBeNull();

        // one item more, and a page follows
        pushed.AddRange(await PushManyAsync(1));
        var first = await ListAsync(AccountRole.Administrator);
        var second = await ListAsync(AccountRole.Administrator, token: first.ContinuationToken);

        first.Count.ShouldBe(50);
        second.Items.ShouldBe([pushed[0]]);
        second.Count.ShouldBe(1);
        second.ContinuationToken.ShouldBeNull();
    }

    [Fact]
    public async Task GetItems_ContinuesBelowTheLastItem_WhateverChangedAboveIt()
    {
        var pushed = await PushManyAsync(60);
        var first = await ListAsync(AccountRole.Administrator);

        // five items arrive and one of the first page is dismissed before the next page is read
        await PushManyAsync(5);
        await DismissAsync(first.Items[0].Id);

        var second = await ListAsync(AccountRole.Administrator, token: first.ContinuationToken);

        second.Items.ShouldBe(Enumerable.Reverse(pushed.Take(10)));
        second.Count.ShouldBe(10);
        second.OpenCount.ShouldBe(64);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task GetItems_WithoutAToken_StartsWithTheNewest(string? token)
    {
        var pushed = await PushManyAsync(2);

        (await ListAsync(AccountRole.Administrator, token: token)).Items.ShouldBe([pushed[1], pushed[0]]);
    }

    [Theory]
    [MemberData(nameof(ForeignTokens))]
    public async Task GetItems_RefusesATokenItDidNotIssue(string token)
    {
        await PushManyAsync(2);

        var exception = await Should.ThrowAsync<InvalidInputException>(() => ListAsync(AccountRole.Administrator, token: token));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidQuery);
        _store.Queries.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetItems_FiltersOpenAndDone()
    {
        var actions = await PushManyAsync(3, ViewInboxItemKind.Action);
        var accepted = await AcceptAsync(actions[0].Id);
        var dismissed = await DismissAsync(actions[2].Id);

        var open = await ListAsync(AccountRole.Administrator);
        var done = await ListAsync(AccountRole.Administrator, ViewInboxFilter.Done);

        open.Items.ShouldBe([actions[1]]);
        open.Count.ShouldBe(1);

        // by the time the items arrived, not by the time they were closed
        done.Items.ShouldBe([dismissed, accepted]);
        done.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetItems_CountsEveryOpenItemTheCallerSees_WhateverThePageAndTheFilter()
    {
        var forAdministrators = await PushManyAsync(55);
        var forContributors = await PushManyAsync(4, role: AccountRole.Contributor);
        await PushManyAsync(1, role: AccountRole.Owner);
        await DismissAsync(forAdministrators[0].Id);
        await DismissAsync(forContributors[0].Id);

        var first = await ListAsync(AccountRole.Administrator);
        var second = await ListAsync(AccountRole.Administrator, token: first.ContinuationToken);
        var done = await ListAsync(AccountRole.Administrator, ViewInboxFilter.Done);

        (first.Count, first.OpenCount).ShouldBe((50, 57));
        (second.Count, second.OpenCount).ShouldBe((7, 57));
        (done.Count, done.OpenCount).ShouldBe((2, 57));

        var open = await ListAsync(AccountRole.Contributor);
        var closed = await ListAsync(AccountRole.Contributor, ViewInboxFilter.Done);

        (open.Count, open.OpenCount).ShouldBe((3, 3));
        (closed.Count, closed.OpenCount).ShouldBe((1, 3));
    }

    [Theory]
    [InlineData(ViewInboxItemKind.Notification)]
    [InlineData(ViewInboxItemKind.Action)]
    [InlineData(ViewInboxItemKind.Message)]
    public async Task Dismiss_ClosesAnOpenItemOfEveryKind(ViewInboxItemKind kind)
    {
        var item = await PushAsync(kind, AccountRole.Contributor);
        _time.Now = Start.AddDays(10);

        var dismissed = await DismissAsync(item.Id, AccountRole.Contributor);

        dismissed.ShouldBe(item with { State = ViewInboxItemState.Dismissed, Closed = AuditStamp.Of(Actor, Start.AddDays(10)) });
        _store.Stored(item.Id).ShouldBe(dismissed);
        _handler.Runs.ShouldBeEmpty();

        // the write gets the time the item still has, so the item ends when it would have ended anyway
        dismissed.ExpiresAt.ShouldBe(Start.AddDays(30));
        _store.Writes[^1].TimeToLive.ShouldBe(TimeSpan.FromDays(20));
    }

    [Fact]
    public async Task AcceptAndDismiss_OfAnAcceptedItem_AreConflictsThatSayAccepted()
    {
        var item = await PushAsync();
        var accepted = await AcceptAsync(item.Id);
        var writes = _store.Writes.Count;

        var dismiss = await Should.ThrowAsync<ConflictException>(() => DismissAsync(item.Id));
        var accept = await Should.ThrowAsync<ConflictException>(() => AcceptAsync(item.Id));

        (dismiss.ErrorCode, dismiss.Message).ShouldBe((ErrorCodes.InboxItemClosed, "The inbox item was already accepted."));
        (accept.ErrorCode, accept.Message).ShouldBe((ErrorCodes.InboxItemClosed, "The inbox item was already accepted."));
        _store.Stored(item.Id).ShouldBe(accepted);
        _store.Writes.Count.ShouldBe(writes);
        _handler.Runs.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AcceptAndDismiss_OfADismissedItem_AreConflictsThatSayDismissed()
    {
        var item = await PushAsync();
        var dismissed = await DismissAsync(item.Id);
        var writes = _store.Writes.Count;

        var dismiss = await Should.ThrowAsync<ConflictException>(() => DismissAsync(item.Id));
        var accept = await Should.ThrowAsync<ConflictException>(() => AcceptAsync(item.Id));

        (dismiss.ErrorCode, dismiss.Message).ShouldBe((ErrorCodes.InboxItemClosed, "The inbox item was already dismissed."));
        (accept.ErrorCode, accept.Message).ShouldBe((ErrorCodes.InboxItemClosed, "The inbox item was already dismissed."));
        _store.Stored(item.Id).ShouldBe(dismissed);
        _store.Writes.Count.ShouldBe(writes);
        _handler.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Accept_ClaimsTheItemBeforeTheHandlerRuns()
    {
        var item = await PushAsync(role: AccountRole.Contributor);
        ViewInboxItem? duringTheRun = null;
        _handler.Run = _ =>
        {
            duringTheRun = _store.Stored(item.Id);
            return ViewInboxActionResult.None;
        };
        _time.Now = Start.AddMinutes(5);

        var accepted = await AcceptAsync(item.Id, AccountRole.Contributor);

        var claimed = item with { State = ViewInboxItemState.Accepted, Closed = AuditStamp.Of(Actor, Start.AddMinutes(5)) };
        accepted.ShouldBe(claimed);
        duringTheRun.ShouldBe(claimed);
        _store.Stored(item.Id).ShouldBe(claimed);
        _handler.Runs.ShouldHaveSingleItem().Context.ShouldBe(new ViewInboxActionContext("acme", "main", "default", claimed, Actor));
        _store.Writes[^1].TimeToLive.ShouldBe(TimeSpan.FromDays(30) - TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task Accept_SavesTheJobOfTheHandlerOnTheItem()
    {
        var item = await PushAsync();
        var job = new ViewInboxJob { Kind = ViewInboxJobKind.ViewExport, Id = "0123", FileName = "export.zip" };
        _handler.Run = _ => new ViewInboxActionResult { Job = job };
        _time.Now = Start.AddDays(1);

        var accepted = await AcceptAsync(item.Id);

        accepted.ShouldBe(item with { State = ViewInboxItemState.Accepted, Closed = AuditStamp.Of(Actor, Start.AddDays(1)), Job = job });
        _store.Stored(item.Id).ShouldBe(accepted);
        _store.Writes[^1].TimeToLive.ShouldBe(TimeSpan.FromDays(29));
    }

    [Fact]
    public async Task Accept_RunsTheHandlerOfTheItemsAction()
    {
        var other = new RecordingActionHandler("Reindex");
        var service = new ViewInboxService(_store, [_handler, other], _time, _logger);
        var item = await _publisher.PushAsync(Push(ViewInboxItemKind.Action) with { Action = "Reindex" });

        await service.AcceptAsync("acme", "main", "default", item.Id, AccountRole.Administrator, Actor);

        other.Runs.ShouldHaveSingleItem().Context.Item.Id.ShouldBe(item.Id);
        _handler.Runs.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Reindex")]
    [InlineData("generateschema")]
    public async Task Accept_OfAnActionWithoutAHandler_IsNotAvailable_AndLeavesTheItemOpen(string action)
    {
        var item = await _publisher.PushAsync(Push(ViewInboxItemKind.Action) with { Action = action });
        var writes = _store.Writes.Count;

        var exception = await Should.ThrowAsync<FeatureNotConfiguredException>(() => AcceptAsync(item.Id));

        exception.ErrorCode.ShouldBe(ErrorCodes.InboxActionNotAvailable);
        _store.Stored(item.Id).ShouldBe(item);
        _store.Writes.Count.ShouldBe(writes);
        _handler.Runs.ShouldBeEmpty();

        (await DismissAsync(item.Id)).State.ShouldBe(ViewInboxItemState.Dismissed);

        // the state is told before the missing handler
        var closed = await Should.ThrowAsync<ConflictException>(() => AcceptAsync(item.Id));

        (closed.ErrorCode, closed.Message).ShouldBe((ErrorCodes.InboxItemClosed, "The inbox item was already dismissed."));
    }

    [Theory]
    [InlineData(ViewInboxItemKind.Notification)]
    [InlineData(ViewInboxItemKind.Message)]
    public async Task Accept_OfAnotherKind_IsAConflict(ViewInboxItemKind kind)
    {
        var item = await PushAsync(kind);

        var open = await Should.ThrowAsync<ConflictException>(() => AcceptAsync(item.Id));

        open.ErrorCode.ShouldBe(ErrorCodes.InboxItemNotAction);
        _store.Stored(item.Id).ShouldBe(item);

        // the kind is told before the state
        await DismissAsync(item.Id);
        var closed = await Should.ThrowAsync<ConflictException>(() => AcceptAsync(item.Id));

        closed.ErrorCode.ShouldBe(ErrorCodes.InboxItemNotAction);
        _handler.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Accept_WhenTheHandlerFails_OpensTheItemAgainAndPassesTheExceptionOn()
    {
        var item = await PushAsync();
        var failure = new ConflictException("The project already has 3 exports or imports in progress; wait until one of them ends.", ErrorCodes.TransferLimit);
        ViewInboxItem? duringTheRun = null;
        _handler.Run = _ =>
        {
            duringTheRun = _store.Stored(item.Id);
            throw failure;
        };
        _time.Now = Start.AddDays(1);

        var exception = await Should.ThrowAsync<ConflictException>(() => AcceptAsync(item.Id));

        exception.ShouldBeSameAs(failure);
        duringTheRun!.State.ShouldBe(ViewInboxItemState.Accepted);
        _store.Stored(item.Id).ShouldBe(item);
        _store.Writes[^1].TimeToLive.ShouldBe(TimeSpan.FromDays(29));
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && ReferenceEquals(entry.Exception, failure));
        (await ListAsync(AccountRole.Administrator)).Items.ShouldBe([item]);

        // nothing started, so the item can be accepted once more
        _handler.Run = _ => ViewInboxActionResult.None;
        (await AcceptAsync(item.Id)).State.ShouldBe(ViewInboxItemState.Accepted);
        _handler.Runs.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Accept_WhenTheItemCannotBeOpenedAgain_StillPassesTheExceptionOfTheHandlerOn()
    {
        var item = await PushAsync();
        var failure = new ConflictException("The project already has 3 exports or imports in progress; wait until one of them ends.", ErrorCodes.TransferLimit);
        _handler.Run = _ => throw failure;
        _store.Replacing = record =>
        {
            if (record.Item.State == ViewInboxItemState.Open)
            {
                throw new IOException("The store is down.");
            }
        };

        var exception = await Should.ThrowAsync<ConflictException>(() => AcceptAsync(item.Id));

        exception.ShouldBeSameAs(failure);
        _store.Stored(item.Id)!.State.ShouldBe(ViewInboxItemState.Accepted);
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Error && entry.Exception is IOException);
    }

    [Fact]
    public async Task Accept_WhenTheJobCannotBeSaved_StillAnswersWithIt()
    {
        var item = await PushAsync();
        var job = new ViewInboxJob { Kind = ViewInboxJobKind.ViewImport, Id = "0123" };
        _handler.Run = _ => new ViewInboxActionResult { Job = job };
        _store.Replacing = record =>
        {
            if (record.Item.Job is not null)
            {
                throw new IOException("The store is down.");
            }
        };

        var accepted = await AcceptAsync(item.Id);

        accepted.State.ShouldBe(ViewInboxItemState.Accepted);
        accepted.Job.ShouldBe(job);
        _store.Stored(item.Id).ShouldBe(accepted with { Job = null });
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Exception is IOException);
    }

    [Fact]
    public async Task Accept_WithATokenCancelledAfterTheClaim_RunsTheHandlerToItsEndAndSavesItsJob()
    {
        var item = await PushAsync();
        var job = new ViewInboxJob { Kind = ViewInboxJobKind.ViewExport, Id = "0123" };
        _handler.Run = _ => new ViewInboxActionResult { Job = job };
        using var caller = new CancellationTokenSource();
        _store.Saved = saved => CancelOnceClaimed(caller, saved);

        var accepted = await AcceptAsync(item.Id, cancellationToken: caller.Token);

        caller.IsCancellationRequested.ShouldBeTrue();
        _handler.Runs.ShouldHaveSingleItem().CancellationToken.CanBeCanceled.ShouldBeFalse();
        accepted.State.ShouldBe(ViewInboxItemState.Accepted);
        accepted.Job.ShouldBe(job);
        _store.Stored(item.Id).ShouldBe(accepted);
    }

    [Fact]
    public async Task Accept_WithATokenCancelledAfterTheClaim_OpensTheItemAgain_WhenTheHandlerFails()
    {
        var item = await PushAsync();
        var failure = new InvalidOperationException("Nothing was started.");
        _handler.Run = _ => throw failure;
        using var caller = new CancellationTokenSource();
        _store.Saved = saved => CancelOnceClaimed(caller, saved);

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => AcceptAsync(item.Id, cancellationToken: caller.Token));

        exception.ShouldBeSameAs(failure);
        caller.IsCancellationRequested.ShouldBeTrue();
        _handler.Runs.ShouldHaveSingleItem().CancellationToken.CanBeCanceled.ShouldBeFalse();
        _store.Stored(item.Id).ShouldBe(item);
    }

    [Fact]
    public async Task Accept_WithATokenCancelledWhileTheClaimIsWritten_StillRunsTheHandler()
    {
        var item = await PushAsync();
        using var caller = new CancellationTokenSource();

        // the caller goes away while the claim is on its way to the store, which saves it all the same
        _store.Replacing = _ => caller.Cancel();

        var accepted = await AcceptAsync(item.Id, cancellationToken: caller.Token);

        caller.IsCancellationRequested.ShouldBeTrue();
        accepted.State.ShouldBe(ViewInboxItemState.Accepted);
        _handler.Runs.ShouldHaveSingleItem().CancellationToken.CanBeCanceled.ShouldBeFalse();
        _store.Stored(item.Id).ShouldBe(accepted);
    }

    [Fact]
    public async Task Accept_WithATokenCancelledBeforeTheClaim_StartsNothing()
    {
        var item = await PushAsync();
        using var caller = new CancellationTokenSource();

        // the caller goes away once the item is read: nothing is claimed for a caller that is gone
        _store.Reading = _ => caller.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => AcceptAsync(item.Id, cancellationToken: caller.Token));

        _store.Stored(item.Id).ShouldBe(item);
        _store.Replaces.ShouldBeEmpty();
        _handler.Runs.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ALostReplace_OfAnItemThatIsGone_IsNotFound(bool accept)
    {
        var item = await PushAsync();

        // the store removes the item between the read and the replace
        _store.Replacing = _ => _store.Remove(item.Id);

        var exception = await Should.ThrowAsync<NotFoundException>(() => CloseAsync(accept, item.Id));

        exception.Message.ShouldBe($"The inbox item '{item.Id}' does not exist or has expired.");
        _store.Replaces.Count.ShouldBe(1);
        _handler.Runs.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ALostReplace_OfAnItemThatExpiredMeanwhile_IsNotFound(bool accept)
    {
        var item = await _publisher.PushAsync(Push(ViewInboxItemKind.Action) with { ExpiresAt = Start.AddHours(1) });

        // another caller changes the item, and its time is over when it is read again
        _store.Replacing = _ =>
        {
            _store.TakeOver(item.Id);
            _time.Now = Start.AddHours(1);
        };

        var exception = await Should.ThrowAsync<NotFoundException>(() => CloseAsync(accept, item.Id));

        exception.Message.ShouldBe($"The inbox item '{item.Id}' does not exist or has expired.");
        _store.Stored(item.Id).ShouldBe(item);
        _store.Replaces.Count.ShouldBe(1);
        _handler.Runs.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true, ViewInboxItemState.Accepted, "The inbox item was already accepted.")]
    [InlineData(true, ViewInboxItemState.Dismissed, "The inbox item was already dismissed.")]
    [InlineData(false, ViewInboxItemState.Accepted, "The inbox item was already accepted.")]
    [InlineData(false, ViewInboxItemState.Dismissed, "The inbox item was already dismissed.")]
    public async Task ALostReplace_OfAnItemThatAnotherCallerClosed_IsAConflictOfThatState(bool accept, ViewInboxItemState state, string message)
    {
        var item = await PushAsync();
        var closed = item with { State = state, Closed = AuditStamp.Of(Other, Start) };

        // the other caller is faster between the read and the replace
        _store.Replacing = _ => _store.TakeOver(item.Id, stored => stored.State == ViewInboxItemState.Open ? closed : stored);

        var exception = await Should.ThrowAsync<ConflictException>(() => CloseAsync(accept, item.Id));

        (exception.ErrorCode, exception.Message).ShouldBe((ErrorCodes.InboxItemClosed, message));
        _store.Stored(item.Id).ShouldBe(closed);
        _store.Replaces.Count.ShouldBe(1);
        _handler.Runs.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ALostReplace_OfAnItemThatIsOpenAgain_IsRepeated(bool accept)
    {
        var item = await PushAsync();

        // another caller accepted the item, its handler failed, and the item is open again
        _store.Replacing = _ =>
        {
            _store.Replacing = null;
            _store.TakeOver(item.Id);
        };

        var closed = await CloseAsync(accept, item.Id);

        closed.ShouldBe(item with
        {
            State = accept ? ViewInboxItemState.Accepted : ViewInboxItemState.Dismissed,
            Closed = AuditStamp.Of(Actor, Start),
        });
        _store.Stored(item.Id).ShouldBe(closed);
        _store.Replaces.Count.ShouldBe(2);
        _handler.Runs.Count.ShouldBe(accept ? 1 : 0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ALostReplace_IsRepeatedThreeTimes(bool accept)
    {
        var item = await PushAsync();
        var lost = 0;
        _store.Replacing = _ =>
        {
            if (lost++ < 3)
            {
                _store.TakeOver(item.Id);
            }
        };

        var closed = await CloseAsync(accept, item.Id);

        closed.State.ShouldBe(accept ? ViewInboxItemState.Accepted : ViewInboxItemState.Dismissed);
        _store.Stored(item.Id).ShouldBe(closed);
        _store.Replaces.Count.ShouldBe(4);
        _handler.Runs.Count.ShouldBe(accept ? 1 : 0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ALostReplace_OfAnItemThatKeepsChanging_IsAConflictAfterThreeRepeats(bool accept)
    {
        var item = await PushAsync();
        _store.Replacing = _ => _store.TakeOver(item.Id);

        var exception = await Should.ThrowAsync<ConflictException>(() => CloseAsync(accept, item.Id));

        (exception.ErrorCode, exception.Message).ShouldBe((ErrorCodes.Conflict, "The inbox item changed; try again."));

        // the first replace and its three repeats
        _store.Replaces.Count.ShouldBe(4);
        _store.Stored(item.Id).ShouldBe(item);
        _handler.Runs.ShouldBeEmpty();
    }

    [Fact]
    public void TwoHandlersOfOneAction_FailTheConstructionOfTheService()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => new ViewInboxService(_store, [_handler, new RecordingActionHandler(HandledAction)], _time, _logger));

        exception.Message.ShouldContain($"'{HandledAction}'");
    }

    [Fact]
    public async Task WithoutAHandler_TheInboxStillListsAndDismisses()
    {
        var service = new ViewInboxService(_store, [], _time, _logger);
        var action = await PushAsync();
        var notification = await PushAsync(ViewInboxItemKind.Notification);

        var open = await service.GetItemsAsync("acme", "main", "default", new ViewInboxQuery { Role = AccountRole.Administrator });
        var dismissed = await service.DismissAsync("acme", "main", "default", notification.Id, AccountRole.Administrator, Actor);
        var exception = await Should.ThrowAsync<FeatureNotConfiguredException>(
            () => service.AcceptAsync("acme", "main", "default", action.Id, AccountRole.Administrator, Actor));

        open.OpenCount.ShouldBe(2);
        dismissed.State.ShouldBe(ViewInboxItemState.Dismissed);
        exception.ErrorCode.ShouldBe(ErrorCodes.InboxActionNotAvailable);
    }

    // The caller goes away as soon as the claim is stored.
    private static void CancelOnceClaimed(CancellationTokenSource caller, ViewInboxRecord saved)
    {
        if (saved.Item.State == ViewInboxItemState.Accepted)
        {
            caller.Cancel();
        }
    }

    // An item of "acme.main.default" pushed now; an action unless the test asks for another kind.
    private Task<ViewInboxItem> PushAsync(ViewInboxItemKind kind = ViewInboxItemKind.Action, AccountRole role = AccountRole.Administrator)
    {
        return _publisher.PushAsync(Push(kind, role));
    }

    // Items pushed a second apart, so that their order is known; oldest first.
    private async Task<List<ViewInboxItem>> PushManyAsync(int count, ViewInboxItemKind kind = ViewInboxItemKind.Notification, AccountRole role = AccountRole.Administrator)
    {
        var items = new List<ViewInboxItem>();

        for (var index = 0; index < count; index++)
        {
            items.Add(await _publisher.PushAsync(Push(kind, role)));
            _time.Now += TimeSpan.FromSeconds(1);
        }

        return items;
    }

    private Task<ViewInboxResponse> ListAsync(AccountRole role, ViewInboxFilter filter = ViewInboxFilter.Open, string? token = null)
    {
        return _service.GetItemsAsync("acme", "main", "default", new ViewInboxQuery { Role = role, Filter = filter, ContinuationToken = token });
    }

    private Task<ViewInboxItem> AcceptAsync(string id, AccountRole role = AccountRole.Administrator, CancellationToken cancellationToken = default)
    {
        return _service.AcceptAsync("acme", "main", "default", id, role, Actor, cancellationToken);
    }

    private Task<ViewInboxItem> DismissAsync(string id, AccountRole role = AccountRole.Administrator, CancellationToken cancellationToken = default)
    {
        return _service.DismissAsync("acme", "main", "default", id, role, Actor, cancellationToken);
    }

    // What accept and dismiss do alike is tested through both.
    private Task<ViewInboxItem> CloseAsync(bool accept, string id)
    {
        return accept ? AcceptAsync(id) : DismissAsync(id);
    }
}
