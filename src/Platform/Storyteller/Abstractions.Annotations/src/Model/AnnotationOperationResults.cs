using System.Collections.Generic;

namespace _42.Platform.Storyteller;

// Body of the deleted-key lookup.
public record class AnnotationKeysRequest
{
    public required IReadOnlyList<string> Keys { get; init; }
}

// Body of the restore: many keys in one request.
public record class AnnotationRestoreRequest
{
    public required IReadOnlyList<string> Keys { get; init; }

    // Also restore what each key's deletion removed below it.
    public bool Descendants { get; init; } = true;
}

public record class AnnotationKeyFailure
{
    public required string Key { get; init; }

    public required string ErrorCode { get; init; }

    public required string Message { get; init; }
}

public record class AnnotationDeletionResult
{
    public required AnnotationDeletion Deletion { get; init; }

    // Descendants first, the root last. Empty when the annotation was already deleted.
    public required IReadOnlyList<string> DeletedKeys { get; init; }
}

public record class AnnotationRestoreResult
{
    // Ancestors first; restored and created.
    public required IReadOnlyList<Annotation> Annotations { get; init; }

    // Ancestors that had to be created.
    public IReadOnlyList<string> CreatedKeys { get; init; } = [];

    public IReadOnlyList<AnnotationKeyFailure> Failed { get; init; } = [];
}

public record class AnnotationPurgeResult
{
    public required IReadOnlyList<string> PurgedKeys { get; init; }
}
