namespace _42.Platform.Storyteller.Annotating;

public class AnnotationOptions
{
    public const string SectionName = "Annotations";

    public const int MinDeletedRetentionDays = 1;
    public const int MaxDeletedRetentionDays = 365;

    // How long a soft-deleted annotation stays restorable before Cosmos removes it through its ttl.
    public int DeletedRetentionDays { get; set; } = 90;
}
