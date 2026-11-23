namespace _42.Platform.Storyteller.Notifying;

public class ViewInboxOptions
{
    public const string SectionName = "ViewInbox";

    public const int MinRetentionDays = 1;
    public const int MaxRetentionDays = 365;

    // How long an item lives when its producer names no ExpiresAt.
    public int RetentionDays { get; set; } = 30;

    public TimeSpan Retention => TimeSpan.FromDays(RetentionDays);
}
