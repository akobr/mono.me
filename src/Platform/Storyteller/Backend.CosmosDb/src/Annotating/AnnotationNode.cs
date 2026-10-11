using _42.Platform.Storyteller.Entities.Annotations;
using _42.Platform.Storyteller.Entities.Configurations;

namespace _42.Platform.Storyteller.Annotating;

// The stored items of one annotation key: the annotation, its configuration and its audit.
internal sealed class AnnotationNode
{
    public AnnotationNode(FullKey key)
    {
        Key = key;
        KeyText = key.Annotation.ToString();
        PartitionKey = key.GetPartitionKey();
    }

    public FullKey Key { get; }

    public string KeyText { get; }

    public string PartitionKey { get; }

    public AnnotationEntity? Entity { get; set; }

    public string? ETag { get; set; }

    public ConfigurationEntity? Configuration { get; set; }

    public AnnotationAuditEntity? Audit { get; set; }

    public string? AuditETag { get; set; }

    public AnnotationItemState State => Entity is null
        ? AnnotationItemState.Missing
        : Entity.Deletion is null
            ? AnnotationItemState.Active
            : AnnotationItemState.Deleted;

    public string AnnotationId => AnnotationItemIds.Annotation(Key.ViewName, KeyText);

    public string ConfigurationId => AnnotationItemIds.Configuration(Key.ViewName, KeyText);

    public string AuditId => AnnotationItemIds.Audit(Key.ViewName, KeyText);
}
