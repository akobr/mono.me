namespace _42.Platform.Storyteller.Sdk.Hosting.Reading;

// The one mapper of what a point read answered to a snapshot. The type and the names come from the key that was read,
// and so does the snapshot's type (SnapshotTypes); the rest comes from the body. The relation names and the strings of a
// unit come from the body only when it is the SDK's type of the key, which its AnnotationType discriminator decides: a
// body without it, or with another one, gives empty lists and nulls. The snapshot's initializers copy labels, values and
// audit stamps, so the SDK's mutable objects stay behind.
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
            Labels = [.. annotation.Labels ?? []],
            Values = (annotation.Values ?? new Dictionary<string, object>()).ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal),
            IsDisabled = annotation.IsDisabled == true,
            ValidFrom = annotation.ValidFrom,
            ExpiresAt = annotation.ExpiresAt,
            Created = annotation.Created,
            Modified = annotation.Modified,
            DeletedAt = annotation.Deletion?.DeletedAt,
        };

        return annotationKey.Type switch
        {
            AnnotationType.Responsibility => new StorytellerResponsibility(
                snapshot,
                StorytellerAnnotation.CopyNames((annotation as Responsibility)?.UnitNames)),
            AnnotationType.Unit => new StorytellerUnit(
                snapshot,
                annotationKey.ResponsibilityName,
                annotationKey.UnitName,
                (annotation as Unit)?.UnitType,
                (annotation as Unit)?.UnitDefinition),
            AnnotationType.Subject => new StorytellerSubject(
                snapshot,
                StorytellerAnnotation.CopyNames((annotation as Subject)?.ContextNames),
                StorytellerAnnotation.CopyNames((annotation as Subject)?.ResponsibilityNames)),
            AnnotationType.Usage => new StorytellerUsage(
                snapshot,
                annotationKey.SubjectName,
                annotationKey.ResponsibilityName,
                StorytellerAnnotation.CopyNames((annotation as Usage)?.ContextNames)),
            AnnotationType.Context => new StorytellerContext(
                snapshot,
                annotationKey.SubjectName,
                annotationKey.ContextName,
                StorytellerAnnotation.CopyNames((annotation as Context)?.ResponsibilityNames)),
            AnnotationType.Execution => new StorytellerExecution(
                snapshot,
                annotationKey.SubjectName,
                annotationKey.ResponsibilityName,
                annotationKey.ContextName,
                StorytellerAnnotation.CopyNames((annotation as Execution)?.UnitNames)),
            AnnotationType.UnitOfExecution => new StorytellerUnitOfExecution(
                snapshot,
                annotationKey.SubjectName,
                annotationKey.ResponsibilityName,
                annotationKey.ContextName,
                annotationKey.UnitName),
            _ => snapshot,
        };
    }
}
