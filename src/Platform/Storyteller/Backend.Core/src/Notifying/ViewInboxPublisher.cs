using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Notifying;

public sealed partial class ViewInboxPublisher : IViewInboxPublisher
{
    private const int MaxTitleLength = 200;
    private const int MaxTextLength = 2000;

    // An item lives at least a minute and at most a year, whatever its producer asks for.
    private static readonly TimeSpan MinLifetime = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(365);

    private readonly IViewInboxStore _store;
    private readonly IOptions<ViewInboxOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<ViewInboxPublisher> _logger;

    public ViewInboxPublisher(
        IViewInboxStore store,
        IOptions<ViewInboxOptions> options,
        TimeProvider time,
        ILogger<ViewInboxPublisher> logger)
    {
        _store = store;

        // Kept unread: a setting outside its range throws when it is read, and a producer that takes the publisher
        // must still be built. The push that needs the retention fails instead.
        _options = options;
        _time = time;
        _logger = logger;
    }

    public async Task<ViewInboxItem> PushAsync(ViewInboxPush push, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(push);
        Validate(push);

        var now = _time.GetUtcNow();
        var createdAt = push.CreatedAt ?? now;
        var record = new ViewInboxRecord
        {
            Organization = push.Organization,
            Project = push.Project,
            View = push.View,
            Item = new ViewInboxItem
            {
                Id = push.Key is null ? ViewInboxIds.NewId(createdAt) : ViewInboxIds.ForKey(createdAt, push.Key),
                Kind = push.Kind,
                Topic = push.Topic,
                Level = push.Level,
                Title = Cut(push.Title, MaxTitleLength)!,
                Text = Cut(push.Text, MaxTextLength),
                MinimalRole = push.MinimalRole,
                State = ViewInboxItemState.Open,
                CreatedAt = createdAt,
                CreatedBy = push.CreatedBy,
                ExpiresAt = Limit(push.ExpiresAt ?? now + _options.Value.Retention, now),
                Action = push.Action,
                Job = push.Job,
            },
        };

        var created = await CreateAsync(record, cancellationToken);

        if (created is not null)
        {
            return created.Item;
        }

        if (push.Key is null)
        {
            throw new InvalidOperationException($"The new inbox item '{record.Item.Id}' exists already.");
        }

        var stored = await _store.GetAsync(record.Organization, record.Project, record.View, record.Item.Id, cancellationToken);

        if (stored is not null)
        {
            // the push of the same event before: nothing is written twice, and an item closed meanwhile stays closed
            return stored.Item;
        }

        // the stored item expired between the create and the read
        _logger.LogInformation(
            "The inbox item {Id} of {Organization}.{Project}.{View} was taken but is gone; it is created again.",
            record.Item.Id,
            record.Organization,
            record.Project,
            record.View);

        created = await CreateAsync(record, cancellationToken);

        if (created is null)
        {
            throw new InvalidOperationException($"The inbox item '{record.Item.Id}' of the key '{push.Key}' can be neither created nor read.");
        }

        return created.Item;
    }

    // The write gets the time the item still has, so no write moves the expiry.
    private Task<ViewInboxRecord?> CreateAsync(ViewInboxRecord record, CancellationToken cancellationToken)
    {
        return _store.TryCreateAsync(record, record.Item.ExpiresAt - _time.GetUtcNow(), cancellationToken);
    }

    // A wrong push is an error of its producer, not of a request.
    private static void Validate(ViewInboxPush push)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(push.Organization);
        ArgumentException.ThrowIfNullOrWhiteSpace(push.Project);
        ArgumentException.ThrowIfNullOrWhiteSpace(push.View);
        ArgumentException.ThrowIfNullOrWhiteSpace(push.Title);

        if (push.Topic is null || !Code().IsMatch(push.Topic))
        {
            throw new ArgumentException($"The topic '{push.Topic}' is not a code: a letter, then up to 63 letters or digits.", nameof(push));
        }

        if (push.Kind == ViewInboxItemKind.Action && push.Action is null)
        {
            throw new ArgumentException("An action needs the code of its handler.", nameof(push));
        }

        if (push.Kind != ViewInboxItemKind.Action && push.Action is not null)
        {
            throw new ArgumentException($"Only an action has the code of a handler, not a {push.Kind.ToString().ToLowerInvariant()}.", nameof(push));
        }

        if (push.Action is not null && !Code().IsMatch(push.Action))
        {
            throw new ArgumentException($"The action '{push.Action}' is not a code: a letter, then up to 63 letters or digits.", nameof(push));
        }

        if (push.MinimalRole < AccountRole.Contributor)
        {
            throw new ArgumentException($"The minimal role of an inbox item is Contributor or above, not {push.MinimalRole}.", nameof(push));
        }

        if (push.Key is not null && push.CreatedAt is null)
        {
            throw new ArgumentException("A push with a key needs the time of its event.", nameof(push));
        }
    }

    // As the jobs cut their messages.
    private static string? Cut(string? text, int length)
    {
        return text is not null && text.Length > length ? $"{text[..(length - 1)]}…" : text;
    }

    private static DateTimeOffset Limit(DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (expiresAt < now + MinLifetime)
        {
            return now + MinLifetime;
        }

        return expiresAt > now + MaxLifetime ? now + MaxLifetime : expiresAt;
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Code();
}
