namespace _42.Platform.Storyteller.Annotating;

public class AnnotationAuditOptions
{
    public const string SectionName = "Audit";

    // Below the 2 MB item limit of Cosmos DB, with room for the system properties.
    public const int MaxAllowedDocumentBytes = 1536 * 1024;

    // Serialized size of the audit document of one annotation; the oldest entries are dropped above it.
    public int MaxDocumentBytes { get; set; } = 64 * 1024;

    // Longest stored string, previous or new, of a field or a primitive value.
    public int MaxValueLength { get; set; } = 256;
}
