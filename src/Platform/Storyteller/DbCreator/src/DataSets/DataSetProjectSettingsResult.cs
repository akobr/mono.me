namespace _42.Platform.Storyteller.DbCreator.DataSets;

// The seed of a set's project-settings.json into the target's project, which a seed never overwrites or merges:
// Created when the project's settings were never set (version 0) and are now those of the set (in a dry run: would
// be), Unchanged when the project has the same settings, Skipped with DocumentExists when it has others (kept), Failed
// with the error code of a write that failed. Settings are the set's.
public sealed record class DataSetProjectSettingsResult(
    ProjectSettings Settings,
    ViewImportOutcome Outcome,
    string? ErrorCode = null,
    string? Message = null);
