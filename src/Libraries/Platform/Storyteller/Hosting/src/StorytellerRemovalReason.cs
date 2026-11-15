namespace _42.Platform.Storyteller.Sdk.Hosting;

// Why a subscription, or an annotation of a target, left, as the annotation that ended it read: the annotation itself,
// or for a target that requires active ancestors, one of them. Disabled, Scheduled and Expired of its own do not end an
// annotation of a target that includes inactive ones; those of an ancestor that the target requires active end it
// whether it includes inactive ones or not.
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

    // The read answered 404: purged, or gone without a trace of a deletion. An ancestor is read without deleted ones, so
    // a deleted ancestor is Missing too.
    Missing,
}
