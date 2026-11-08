namespace _42.Platform.Storyteller.Sdk.Hosting;

// Why a subscription, or an annotation of a target, left, as the annotation that ended it read. Disabled, Scheduled and
// Expired do not end an annotation of a target that includes inactive ones.
public enum StorytellerRemovalReason
{
    // Soft-deleted; restoring it adds it again.
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
