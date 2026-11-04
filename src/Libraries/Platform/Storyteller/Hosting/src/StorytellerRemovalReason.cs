namespace _42.Platform.Storyteller.Sdk.Hosting;

// Why a subscription ended, as the annotation that ended it read.
public enum StorytellerRemovalReason
{
    // Soft-deleted; restoring it adds the subscription again.
    Deleted = 0,

    // IsDisabled is set.
    Disabled,

    // ValidFrom is after now.
    Scheduled,

    // ExpiresAt is at or before now.
    Expired,

    // The read answered 404: purged, or gone without a trace of a deletion.
    Missing,
}
