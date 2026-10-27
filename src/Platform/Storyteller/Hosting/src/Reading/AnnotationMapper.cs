namespace _42.Platform.Storyteller.Sdk.Hosting.Reading;

// The one mapper of what a point read answered to a snapshot. The type and the names come from the key that was read,
// the rest from the body; values and audit stamps are copied, so the SDK's mutable objects stay behind.
internal static class AnnotationMapper
{
    // key: as read, the type code lower-cased.
    public static StorytellerAnnotation Map(FullKey key, Annotation annotation)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(annotation);

        var annotationKey = key.Annotation;
        var snapshot = new StorytellerAnnotation
        {
            Key = key.ToString(),
            AnnotationKey = annotationKey.ToString(),
            Type = annotationKey.Type,
            Name = annotationKey.Name,
            Title = annotation.Title,
            Description = annotation.Description,
            Labels = Copy(annotation.Labels),
            Values = StorytellerAnnotation.CopyValues(annotation.Values),
            IsDisabled = annotation.IsDisabled == true,
            ValidFrom = annotation.ValidFrom,
            ExpiresAt = annotation.ExpiresAt,
            Created = StorytellerAnnotation.CopyStamp(annotation.Created),
            Modified = StorytellerAnnotation.CopyStamp(annotation.Modified),
            DeletedAt = annotation.Deletion?.DeletedAt,
        };

        return annotationKey.Type == AnnotationType.Execution
            ? new StorytellerExecution(
                snapshot,
                annotationKey.SubjectName,
                annotationKey.ResponsibilityName,
                annotationKey.ContextName,
                Copy((annotation as Execution)?.UnitNames))
            : snapshot;
    }

    private static IReadOnlyList<string> Copy(IEnumerable<string>? items)
    {
        return Array.AsReadOnly(items?.ToArray() ?? []);
    }
}
