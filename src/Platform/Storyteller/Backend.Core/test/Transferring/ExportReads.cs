namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

// The reads of an export in their order, shared by the fakes of a test.
internal sealed class ExportReads
{
    public List<(ViewArchiveContent Content, string Organization, string Project, string View)> Reads { get; } = [];

    public void Add(ViewArchiveContent content, string organization, string project, string view) => Reads.Add((content, organization, project, view));
}
