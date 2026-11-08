namespace _42.Platform.Storyteller.Annotating;

// The condition of an annotation write. IfMatch holds when the annotation exists (is not deleted) and its version is
// matched; IfNoneMatch holds when it does not exist or its version is not matched. Both are checked when both are
// given, so If-Match with If-None-Match: * never holds. An empty condition always holds.
public sealed record AnnotationWriteCondition
{
    public static AnnotationWriteCondition None { get; } = new();

    // If-None-Match: *, a write that only creates.
    public static AnnotationWriteCondition CreateOnly { get; } = new() { IfNoneMatch = AnnotationVersionMatch.Anything };

    public AnnotationVersionMatch? IfMatch { get; init; }

    public AnnotationVersionMatch? IfNoneMatch { get; init; }

    public bool IsEmpty => IfMatch is null && IfNoneMatch is null;

    // Whether a missing or deleted key passes, so the write may create it.
    public bool AllowsCreate => IfMatch is null;

    public static AnnotationWriteCondition Matching(params string[] markers) => new() { IfMatch = AnnotationVersionMatch.Of(markers) };

    // exists: the annotation item exists and is not deleted; marker: its version.
    public bool Holds(bool exists, string? marker)
    {
        if (IfMatch is { } ifMatch && !(exists && ifMatch.Matches(marker)))
        {
            return false;
        }

        return IfNoneMatch is not { } ifNoneMatch || !(exists && ifNoneMatch.Matches(marker));
    }

    // Throws PreconditionFailedException (412) when the condition does not hold.
    public void Check(AnnotationKey key, bool exists, string? marker)
    {
        if (Holds(exists, marker))
        {
            return;
        }

        // the message of the part that failed: If-Match is checked first
        var ifMatchFailed = IfMatch is { } ifMatch && !(exists && ifMatch.Matches(marker));
        var message = ifMatchFailed
            ? exists
                ? $"The annotation '{key}' changed since it was read (If-Match); read it again and repeat the write."
                : $"The annotation '{key}' does not exist or is deleted (If-Match)."
            : IfNoneMatch is { Any: true }
                ? $"The annotation '{key}' already exists (If-None-Match); read it and update it instead."
                : $"The annotation '{key}' is at a version that If-None-Match names.";

        throw new PreconditionFailedException(message, ErrorCodes.AnnotationPreconditionFailed);
    }
}
