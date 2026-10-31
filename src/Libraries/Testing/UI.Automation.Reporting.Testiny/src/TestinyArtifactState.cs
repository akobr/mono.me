namespace _42.Testing.UI.Automation.Reporting.Testiny;

// What happened to an artifact of a result, as its comment lists it.
internal enum TestinyArtifactState
{
    Attached,

    // Reporting:Testiny:Attachments does not upload the files of this outcome.
    NotAttached,

    // Over Reporting:Testiny:MaxAttachmentBytes.
    TooLarge,

    // The file is gone, e.g. a replay on another machine.
    Missing,

    // Testiny refused it, or it failed transiently TestinyTestRunReporter.MaxUploadAttempts times.
    UploadFailed,
}
