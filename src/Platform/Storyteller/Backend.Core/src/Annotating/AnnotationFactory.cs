namespace _42.Platform.Storyteller.Annotating;

// Builds annotation models whose key-derived members always agree with the key.
public static class AnnotationFactory
{
    public const string DefaultUnitType = "event";
    public const string DefaultUnitDefinition = "unknown";

    // A model of the key's runtime type with the members derived from the key and empty relation arrays.
    // With copyFrom, it takes the members an automatic ancestor copies from its descendant: the validity window,
    // the time zone, the disabled flag and the labels.
    public static Annotation CreateDefault(AnnotationKey key, string projectName, string viewName, Annotation? copyFrom = null)
    {
        var annotation = Create(key, projectName, viewName);

        return copyFrom is null
            ? annotation
            : annotation with
            {
                ValidFrom = copyFrom.ValidFrom,
                ExpiresAt = copyFrom.ExpiresAt,
                TimeZone = copyFrom.TimeZone,
                IsDisabled = copyFrom.IsDisabled,
                Labels = copyFrom.Labels,
            };
    }

    // The editable content of the input in a model of the key's runtime type. Everything the key decides (type,
    // name, ancestor keys and names) comes from the key, and the relation arrays are left empty: the server owns them.
    public static Annotation Normalize(Annotation input, AnnotationKey key)
    {
        var annotation = Create(key, input.ProjectName, input.ViewName) with
        {
            IsDisabled = input.IsDisabled,
            ValidFrom = input.ValidFrom,
            ExpiresAt = input.ExpiresAt,
            TimeZone = input.TimeZone,
            Title = input.Title,
            Description = input.Description,
            DocumentationLink = input.DocumentationLink,
            Labels = input.Labels,
            Values = input.Values,
        };

        return annotation is Unit unit && input is IUnit inputUnit
            ? unit with { UnitType = inputUnit.UnitType, UnitDefinition = inputUnit.UnitDefinition }
            : annotation;
    }

    private static Annotation Create(AnnotationKey key, string projectName, string viewName)
    {
        var keyText = key.ToString();

        return key.Type switch
        {
            AnnotationType.Responsibility => new Responsibility
            {
                ProjectName = projectName,
                ViewName = viewName,
                AnnotationKey = keyText,
                Name = key.Name,
                AnnotationType = key.Type,
            },
            AnnotationType.Subject => new Subject
            {
                ProjectName = projectName,
                ViewName = viewName,
                AnnotationKey = keyText,
                Name = key.Name,
                AnnotationType = key.Type,
            },
            AnnotationType.Unit => new Unit
            {
                ProjectName = projectName,
                ViewName = viewName,
                AnnotationKey = keyText,
                Name = key.Name,
                AnnotationType = key.Type,
                ResponsibilityKey = key.GetResponsibilityKey(),
                ResponsibilityName = key.GetResponsibilityName(),
                UnitType = DefaultUnitType,
                UnitDefinition = DefaultUnitDefinition,
            },
            AnnotationType.Usage => new Usage
            {
                ProjectName = projectName,
                ViewName = viewName,
                AnnotationKey = keyText,
                Name = key.Name,
                AnnotationType = key.Type,
                SubjectKey = key.GetSubjectKey(),
                SubjectName = key.GetSubjectName(),
                ResponsibilityKey = key.GetResponsibilityKey(),
                ResponsibilityName = key.GetResponsibilityName(),
            },
            AnnotationType.Context => new Context
            {
                ProjectName = projectName,
                ViewName = viewName,
                AnnotationKey = keyText,
                Name = key.Name,
                AnnotationType = key.Type,
                SubjectKey = key.GetSubjectKey(),
                SubjectName = key.GetSubjectName(),
            },
            AnnotationType.Execution => new Execution
            {
                ProjectName = projectName,
                ViewName = viewName,
                AnnotationKey = keyText,
                Name = key.Name,
                AnnotationType = key.Type,
                SubjectKey = key.GetSubjectKey(),
                SubjectName = key.GetSubjectName(),
                ResponsibilityKey = key.GetResponsibilityKey(),
                ResponsibilityName = key.GetResponsibilityName(),
                ContextKey = key.GetContextKey(),
                ContextName = key.GetContextName(),
                UsageKey = key.GetUsageKey(),
            },
            AnnotationType.UnitOfExecution => new UnitOfExecution
            {
                ProjectName = projectName,
                ViewName = viewName,
                AnnotationKey = keyText,
                Name = key.Name,
                AnnotationType = key.Type,
                SubjectKey = key.GetSubjectKey(),
                SubjectName = key.GetSubjectName(),
                ResponsibilityKey = key.GetResponsibilityKey(),
                ResponsibilityName = key.GetResponsibilityName(),
                ContextKey = key.GetContextKey(),
                ContextName = key.GetContextName(),
                UnitKey = key.GetUnitKey(),
                UnitName = key.GetUnitName(),
                UsageKey = key.GetUsageKey(),
                ExecutionKey = key.GetExecutionKey(),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(key), key.Type, "Unknown annotation type."),
        };
    }
}
