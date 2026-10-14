namespace _42.Platform.Storyteller.Transferring;

public class ConfigurationTransferOptions
{
    public const string SectionName = "ConfigurationTransfers";

    public const int MinRetentionHours = 1;
    public const int MaxRetentionHours = 168;

    // Below the 2 MB item limit of Cosmos DB: one document is one configuration item.
    public const int MaxAllowedDocumentBytes = 2 * 1024 * 1024;

    // How long a job and its archive stay after the job's last change.
    public int RetentionHours { get; set; } = 24;

    // Queued or running jobs of one project.
    public int MaxActiveTransfers { get; set; } = 3;

    // Size of an uploaded zip, below the 30 MB request limit of Kestrel.
    public int MaxArchiveBytes { get; set; } = 20 * 1024 * 1024;

    // Uncompressed size of all configuration files of an upload, from the zip's central directory.
    public long MaxExtractedBytes { get; set; } = 256L * 1024 * 1024;

    // Uncompressed size of one configuration file.
    public int MaxDocumentBytes { get; set; } = MaxAllowedDocumentBytes;

    // Entries of an uploaded zip, directories and ignored files included.
    public int MaxArchiveEntries { get; set; } = 50_000;

    // Items kept in an import job; the summary counts every file.
    public int MaxReportedItems { get; set; } = 1_000;

    public TimeSpan Retention => TimeSpan.FromHours(RetentionHours);
}
