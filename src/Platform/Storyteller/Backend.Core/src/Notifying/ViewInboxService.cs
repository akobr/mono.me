using System.Text;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Notifying;

public sealed class ViewInboxService : IViewInboxService
{
    private const string TokenVersion = "1";
    private const string InvalidTokenMessage = "The continuation token is not one of the inbox; start again without it.";

    // How often a lost replace is repeated for an item that is open again.
    private const int MaxReplaceRepeats = 3;

    private readonly IViewInboxStore _store;
    private readonly Dictionary<string, IViewInboxActionHandler> _handlers = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;
    private readonly ILogger<ViewInboxService> _logger;

    public ViewInboxService(
        IViewInboxStore store,
        IEnumerable<IViewInboxActionHandler> handlers,
        TimeProvider time,
        ILogger<ViewInboxService> logger)
    {
        _store = store;
        _time = time;
        _logger = logger;

        foreach (var handler in handlers)
        {
            // which of the two would run is not a question to answer at the first acceptance
            if (!_handlers.TryAdd(handler.Action, handler))
            {
                throw new InvalidOperationException(
                    $"The inbox action '{handler.Action}' has two handlers: {_handlers[handler.Action].GetType().Name} and {handler.GetType().Name}.");
            }
        }
    }

    public static string EncodeToken(string lastId)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{TokenVersion}:{lastId}"))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    // The last id of the page before, or null for the first page. InvalidQuery for anything this inbox did not issue.
    public static string? DecodeToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        string text;

        try
        {
            var base64 = token.Trim().Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            text = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            throw new InvalidInputException(InvalidTokenMessage, ErrorCodes.InvalidQuery);
        }

        var separator = text.IndexOf(':', StringComparison.Ordinal);

        if (separator < 0
            || !string.Equals(text[..separator], TokenVersion, StringComparison.Ordinal)
            || !ViewInboxIds.IsValid(text[(separator + 1)..]))
        {
            throw new InvalidInputException(InvalidTokenMessage, ErrorCodes.InvalidQuery);
        }

        return text[(separator + 1)..];
    }

    public async Task<ViewInboxResponse> GetItemsAsync(
        string organization,
        string project,
        string view,
        ViewInboxQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var before = DecodeToken(query.ContinuationToken);
        var roles = GetVisibleRoles(query.Role);

        if (roles.Count == 0)
        {
            return new ViewInboxResponse { Items = [] };
        }

        // One more than a page tells whether another page follows.
        var records = _store.QueryAsync(organization, project, view, query.Filter, roles, before, ViewInboxQuery.PageSize + 1, cancellationToken);
        var openCount = _store.CountOpenAsync(organization, project, view, roles, cancellationToken);
        await Task.WhenAll(records, openCount);

        var page = (await records).Take(ViewInboxQuery.PageSize).Select(record => record.Item).ToList();

        return new ViewInboxResponse
        {
            Items = page,
            ContinuationToken = (await records).Count > ViewInboxQuery.PageSize ? EncodeToken(page[^1].Id) : null,
            Count = page.Count,
            OpenCount = await openCount,
        };
    }

    public async Task<ViewInboxItem> AcceptAsync(
        string organization,
        string project,
        string view,
        string id,
        AccountRole role,
        AuditActor actor,
        CancellationToken cancellationToken = default)
    {
        var record = await GetVisibleAsync(organization, project, view, id, role, cancellationToken);

        if (record.Item.Kind != ViewInboxItemKind.Action)
        {
            throw new ConflictException("The inbox item is not an action; only an action can be accepted.", ErrorCodes.InboxItemNotAction);
        }

        if (record.Item.State != ViewInboxItemState.Open)
        {
            throw Closed(record.Item);
        }

        if (record.Item.Action is null || !_handlers.TryGetValue(record.Item.Action, out var handler))
        {
            throw new FeatureNotConfiguredException(
                $"The action '{record.Item.Action}' of the inbox item is not available in this deployment.",
                ErrorCodes.InboxActionNotAvailable);
        }

        // The claim is the point of no return, and it starts with its write: a write that reaches the store while its
        // caller goes away would leave an accepted item without its work. So a call that is already cancelled starts
        // nothing, and from the claim on nothing listens to the caller's token.
        cancellationToken.ThrowIfCancellationRequested();

        // claimed before the work starts, so two people cannot start it twice
        var claimed = await CloseAsync(record, ViewInboxItemState.Accepted, role, actor, CancellationToken.None);

        ViewInboxActionResult? result;

        try
        {
            result = await handler.RunAsync(new ViewInboxActionContext(organization, project, view, claimed.Item, actor), CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "The action {Action} of the inbox item {Id} of {Organization}.{Project}.{View} failed; the item is opened again.",
                claimed.Item.Action,
                id,
                organization,
                project,
                view);

            // a handler throws only when it started nothing, so the item can be accepted once more
            await ReopenAsync(claimed);
            throw;
        }

        return result?.Job is null ? claimed.Item : await SaveJobAsync(claimed, result.Job);
    }

    public async Task<ViewInboxItem> DismissAsync(
        string organization,
        string project,
        string view,
        string id,
        AccountRole role,
        AuditActor actor,
        CancellationToken cancellationToken = default)
    {
        var record = await GetVisibleAsync(organization, project, view, id, role, cancellationToken);

        if (record.Item.State != ViewInboxItemState.Open)
        {
            throw Closed(record.Item);
        }

        var dismissed = await CloseAsync(record, ViewInboxItemState.Dismissed, role, actor, cancellationToken);
        return dismissed.Item;
    }

    // The item as the caller may see it. The role is looked at before the kind and the state, and an expired item
    // counts as missing whenever the store removes it: every such item answers as an unknown id does.
    private async Task<ViewInboxRecord> GetVisibleAsync(
        string organization,
        string project,
        string view,
        string id,
        AccountRole role,
        CancellationToken cancellationToken)
    {
        // an id of another shape never reaches the store
        var record = ViewInboxIds.IsValid(id)
            ? await _store.GetAsync(organization, project, view, id, cancellationToken)
            : null;

        if (record is null || record.Item.ExpiresAt <= _time.GetUtcNow() || !IsVisible(record.Item, role))
        {
            throw new NotFoundException($"The inbox item '{id}' does not exist or has expired.");
        }

        return record;
    }

    // Saves the open item as accepted or dismissed, guarded by its ETag. The store answers null for a changed ETag and
    // for a removed item, so a lost replace reads the item again: gone is not found, closed is a conflict, and open
    // again (the handler of another caller's acceptance failed) repeats the replace.
    private async Task<ViewInboxRecord> CloseAsync(
        ViewInboxRecord record,
        ViewInboxItemState state,
        AccountRole role,
        AuditActor actor,
        CancellationToken cancellationToken)
    {
        var current = record;
        var repeats = 0;

        while (true)
        {
            var now = _time.GetUtcNow();

            // an item that expired since it was read is missing, also for its handler
            if (current.Item.ExpiresAt <= now)
            {
                throw new NotFoundException($"The inbox item '{current.Item.Id}' does not exist or has expired.");
            }

            var saved = await _store.TryReplaceAsync(
                current with { Item = current.Item with { State = state, Closed = AuditStamp.Of(actor, now) } },
                current.Item.ExpiresAt - now,
                cancellationToken);

            if (saved is not null)
            {
                return saved;
            }

            current = await GetVisibleAsync(record.Organization, record.Project, record.View, record.Item.Id, role, cancellationToken);

            if (current.Item.State != ViewInboxItemState.Open)
            {
                throw Closed(current.Item);
            }

            if (repeats == MaxReplaceRepeats)
            {
                throw new ConflictException("The inbox item changed; try again.");
            }

            repeats++;
        }
    }

    // Best effort: an item that stays accepted without its work is the price of a failure here.
    private async Task ReopenAsync(ViewInboxRecord claimed)
    {
        try
        {
            var reopened = await _store.TryReplaceAsync(
                claimed with { Item = claimed.Item with { State = ViewInboxItemState.Open, Closed = null } },
                claimed.Item.ExpiresAt - _time.GetUtcNow(),
                CancellationToken.None);

            if (reopened is null)
            {
                _logger.LogWarning(
                    "The inbox item {Id} of {Organization}.{Project}.{View} was not opened again after its action failed: it changed or expired.",
                    claimed.Item.Id,
                    claimed.Organization,
                    claimed.Project,
                    claimed.View);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "The inbox item {Id} of {Organization}.{Project}.{View} was not opened again after its action failed; it stays accepted.",
                claimed.Item.Id,
                claimed.Organization,
                claimed.Project,
                claimed.View);
        }
    }

    // Best effort: the job runs whether the item names it or not, so the caller gets it either way.
    private async Task<ViewInboxItem> SaveJobAsync(ViewInboxRecord claimed, ViewInboxJob job)
    {
        var item = claimed.Item with { Job = job };

        try
        {
            var saved = await _store.TryReplaceAsync(claimed with { Item = item }, item.ExpiresAt - _time.GetUtcNow(), CancellationToken.None);

            if (saved is null)
            {
                _logger.LogWarning(
                    "The job {JobId} was not saved on the inbox item {Id} of {Organization}.{Project}.{View}: the item changed or expired.",
                    job.Id,
                    item.Id,
                    claimed.Organization,
                    claimed.Project,
                    claimed.View);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "The job {JobId} was not saved on the inbox item {Id} of {Organization}.{Project}.{View}.",
                job.Id,
                item.Id,
                claimed.Organization,
                claimed.Project,
                claimed.View);
        }

        return item;
    }

    // A caller sees the items whose minimal role is between Contributor and the caller's own.
    private static bool IsVisible(ViewInboxItem item, AccountRole role)
    {
        return item.MinimalRole >= AccountRole.Contributor && item.MinimalRole <= role;
    }

    private static List<AccountRole> GetVisibleRoles(AccountRole role)
    {
        return Enum.GetValues<AccountRole>().Where(candidate => candidate >= AccountRole.Contributor && candidate <= role).ToList();
    }

    private static ConflictException Closed(ViewInboxItem item)
    {
        return new ConflictException(
            item.State == ViewInboxItemState.Accepted ? "The inbox item was already accepted." : "The inbox item was already dismissed.",
            ErrorCodes.InboxItemClosed);
    }
}
