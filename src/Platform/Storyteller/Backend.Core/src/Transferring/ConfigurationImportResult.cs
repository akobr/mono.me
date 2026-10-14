namespace _42.Platform.Storyteller.Transferring;

// The outcome of importing one document; a dry run reports Created and Updated without a version.
public sealed record class ConfigurationImportResult(
    ConfigurationImportOutcome Outcome,
    ulong? Version = null,
    string? ErrorCode = null,
    string? Message = null);
