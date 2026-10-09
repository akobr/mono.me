namespace _42.Platform.Storyteller.Configuring;

public enum JsonPatchFailureKind
{
    // The document is not a valid RFC 6902 patch: not an array, unknown op, missing path or value.
    Invalid,

    // A test operation did not match the current document. The client's view of the document is stale.
    TestFailed,

    // Another operation could not be applied, for example its path does not exist.
    OperationFailed,
}

// Derives from InvalidOperationException, so callers that treat any invalid input as 400 keep working.
public sealed class JsonPatchException(string message, JsonPatchFailureKind kind, int? operationIndex = null)
    : InvalidOperationException(message)
{
    public JsonPatchFailureKind Kind { get; } = kind;

    public int? OperationIndex { get; } = operationIndex;

    public string ErrorCode => Kind == JsonPatchFailureKind.TestFailed
        ? ErrorCodes.PatchTestFailed
        : ErrorCodes.PatchInvalid;
}
