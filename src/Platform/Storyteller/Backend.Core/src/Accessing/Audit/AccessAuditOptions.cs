namespace _42.Platform.Storyteller.Accessing;

public class AccessAuditOptions
{
    // "Audit" is the section of the annotation audit.
    public const string SectionName = "AccessAudit";

    public const int MinRetentionDays = 1;
    public const int MaxRetentionDays = 3650;

    // How long an access audit record is kept before Cosmos removes it through its ttl: two years.
    public int RetentionDays { get; set; } = 730;
}
