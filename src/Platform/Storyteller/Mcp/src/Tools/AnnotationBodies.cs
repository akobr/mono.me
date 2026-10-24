namespace _42.Platform.Storyteller.Mcp.Tools;

// Bodies of annotation writes. The SDK's Annotation is polymorphic (the discriminator AnnotationType comes from the
// runtime type) and the API requires every member the key decides, so the body is built from the key, as the API's
// AnnotationFactory does: the subtype, the name and the keys and names of the ancestors.
public static class AnnotationBodies
{
    public static Sdk.Annotation Create(AnnotationKey key, string project, string view)
    {
        Sdk.Annotation annotation = key.Type switch
        {
            AnnotationType.Responsibility => new Sdk.Responsibility(),
            AnnotationType.Subject => new Sdk.Subject(),
            AnnotationType.Unit => new Sdk.Unit
            {
                ResponsibilityKey = key.GetResponsibilityKey().ToString(),
                ResponsibilityName = key.GetResponsibilityName(),
            },
            AnnotationType.Usage => new Sdk.Usage
            {
                SubjectKey = key.GetSubjectKey().ToString(),
                SubjectName = key.GetSubjectName(),
                ResponsibilityKey = key.GetResponsibilityKey().ToString(),
                ResponsibilityName = key.GetResponsibilityName(),
            },
            AnnotationType.Context => new Sdk.Context
            {
                SubjectKey = key.GetSubjectKey().ToString(),
                SubjectName = key.GetSubjectName(),
            },
            AnnotationType.Execution => new Sdk.Execution
            {
                SubjectKey = key.GetSubjectKey().ToString(),
                SubjectName = key.GetSubjectName(),
                ResponsibilityKey = key.GetResponsibilityKey().ToString(),
                ResponsibilityName = key.GetResponsibilityName(),
                ContextKey = key.GetContextKey().ToString(),
                ContextName = key.GetContextName(),
                UsageKey = key.GetUsageKey().ToString(),
            },
            AnnotationType.UnitOfExecution => new Sdk.UnitOfExecution
            {
                SubjectKey = key.GetSubjectKey().ToString(),
                SubjectName = key.GetSubjectName(),
                ResponsibilityKey = key.GetResponsibilityKey().ToString(),
                ResponsibilityName = key.GetResponsibilityName(),
                ContextKey = key.GetContextKey().ToString(),
                ContextName = key.GetContextName(),
                UnitKey = key.GetUnitKey().ToString(),
                UnitName = key.GetUnitName(),
                UsageKey = key.GetUsageKey().ToString(),
                ExecutionKey = key.GetExecutionKey().ToString(),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(key), key.Type, "Unknown annotation type."),
        };

        annotation.ProjectName = project;
        annotation.ViewName = view;
        annotation.AnnotationKey = key.ToString();
        annotation.Name = key.Name;
        return annotation;
    }

    // The members a write may change; the server owns the rest (stamps, deletion, relation arrays).
    public static void CopyEditable(Sdk.Annotation from, Sdk.Annotation to)
    {
        to.Title = from.Title;
        to.Description = from.Description;
        to.DocumentationLink = from.DocumentationLink;
        to.Labels = from.Labels is null ? null : [.. from.Labels];
        to.Values = from.Values is null ? null : new Dictionary<string, object>(from.Values);
        to.IsDisabled = from.IsDisabled;
        to.ValidFrom = from.ValidFrom;
        to.ExpiresAt = from.ExpiresAt;
        to.TimeZone = from.TimeZone;

        if (from is Sdk.Unit fromUnit && to is Sdk.Unit toUnit)
        {
            toUnit.UnitType = fromUnit.UnitType;
            toUnit.UnitDefinition = fromUnit.UnitDefinition;
        }
    }

    // The type of an annotation read from the API: its runtime subtype, or its key when the discriminator was unknown.
    public static string TypeName(Sdk.Annotation annotation)
    {
        return annotation switch
        {
            Sdk.Responsibility => nameof(AnnotationType.Responsibility),
            Sdk.Unit => nameof(AnnotationType.Unit),
            Sdk.Subject => nameof(AnnotationType.Subject),
            Sdk.Usage => nameof(AnnotationType.Usage),
            Sdk.Context => nameof(AnnotationType.Context),
            Sdk.Execution => nameof(AnnotationType.Execution),
            Sdk.UnitOfExecution => nameof(AnnotationType.UnitOfExecution),
            _ => AnnotationKey.TryParse(annotation.AnnotationKey ?? string.Empty, out var key) ? key.Type.ToString() : string.Empty,
        };
    }
}
