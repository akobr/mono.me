namespace _42.Platform.Storyteller.Entities.Configurations;

// The stamp of a version, also for items stored before the stamps: their author string and their time
// (the history's CreationTime, or the item's last write).
public static class ConfigurationStampExtensions
{
    public static AuditStamp GetModified(this ConfigurationEntity @this)
    {
        return @this.Modified ?? LegacyAuthors.ToStamp(@this.Author, @this.GetLastUpdatedTime());
    }

    public static AuditStamp GetModified(this ConfigurationHistoryEntity @this)
    {
        return @this.Modified ?? LegacyAuthors.ToStamp(@this.Author, @this.CreationTime ?? @this.GetLastUpdatedTime());
    }

    public static AuditStamp GetModified(this ConfigurationSchemaEntity @this)
    {
        return @this.Modified ?? LegacyAuthors.ToStamp(@this.Author, @this.GetLastUpdatedTime());
    }

    public static AuditStamp GetModified(this ConfigurationSchemaHistoryEntity @this)
    {
        return @this.Modified ?? LegacyAuthors.ToStamp(@this.Author, @this.CreationTime ?? @this.GetLastUpdatedTime());
    }

    public static AuditStamp GetModified(this GenerateTemplateEntity @this)
    {
        return @this.Modified ?? LegacyAuthors.ToStamp(@this.Author, @this.GetLastUpdatedTime());
    }

    public static AuditStamp GetModified(this GenerateTemplateHistoryEntity @this)
    {
        return @this.Modified ?? LegacyAuthors.ToStamp(@this.Author, @this.CreationTime ?? @this.GetLastUpdatedTime());
    }
}
