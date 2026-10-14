namespace _42.Platform.Storyteller;

// Whether an annotation is in effect now (AnnotationStatusExtensions). The reads return only active annotations
// unless they are asked for the others. Deletion is a separate state (AnnotationState).
public enum AnnotationStatus
{
    Active = 0,

    // IsDisabled is set.
    Disabled,

    // ValidFrom is after now.
    Scheduled,

    // ExpiresAt is at or before now.
    Expired,
}
