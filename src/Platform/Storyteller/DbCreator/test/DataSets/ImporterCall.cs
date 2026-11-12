using _42.Platform.Storyteller.Transferring;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

internal sealed record class ImporterCall(
    string Organization,
    string Project,
    string View,
    IReadOnlyList<ViewArchiveFile> Files,
    ViewImportOptions Options,
    ViewImportOverlay Overlay,
    IReadOnlySet<string> ActiveAtStart,
    IReadOnlySet<string> FailedAtStart,
    IReadOnlySet<string> ConfigurationsAtStart);
