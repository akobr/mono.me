using _42.Platform.Storyteller.Notifying;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Notifying;

// Keeps what a producer pushed and the token it pushed with. It pushes on to a real publisher when a test gives it one,
// and fails every push when a test asks for that.
internal sealed class RecordingViewInboxPublisher(IViewInboxPublisher? inner = null) : IViewInboxPublisher
{
    public List<(ViewInboxPush Push, CancellationToken CancellationToken)> Pushes { get; } = [];

    // The item of every push that was answered.
    public List<ViewInboxItem> Items { get; } = [];

    public Exception? Failure { get; set; }

    public async Task<ViewInboxItem> PushAsync(ViewInboxPush push, CancellationToken cancellationToken = default)
    {
        Pushes.Add((push, cancellationToken));

        if (Failure is not null)
        {
            throw Failure;
        }

        var item = inner is null ? ItemOf(push) : await inner.PushAsync(push, cancellationToken);
        Items.Add(item);
        return item;
    }

    // An open item of the push, enough for a producer that only pushes.
    private static ViewInboxItem ItemOf(ViewInboxPush push)
    {
        var createdAt = push.CreatedAt ?? DateTimeOffset.UnixEpoch;

        return new ViewInboxItem
        {
            Id = push.Key is null ? ViewInboxIds.NewId(createdAt) : ViewInboxIds.ForKey(createdAt, push.Key),
            Kind = push.Kind,
            Topic = push.Topic,
            Level = push.Level,
            Title = push.Title,
            Text = push.Text,
            MinimalRole = push.MinimalRole,
            State = ViewInboxItemState.Open,
            CreatedAt = createdAt,
            CreatedBy = push.CreatedBy,
            ExpiresAt = push.ExpiresAt ?? createdAt.AddDays(30),
            Action = push.Action,
            Job = push.Job,
        };
    }
}
