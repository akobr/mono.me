using System;
using _42.Platform.Storyteller.Entities.Annotations;

namespace _42.Platform.Storyteller.Annotating;

public static class CosmosTypeCodes
{
    public static Type GetEntityType(AnnotationType type)
    {
        return type switch
        {
            AnnotationType.Responsibility => typeof(ResponsibilityEntity),
            AnnotationType.Subject => typeof(SubjectEntity),
            AnnotationType.Usage => typeof(UsageEntity),
            AnnotationType.Context => typeof(ContextEntity),
            AnnotationType.Execution => typeof(ExecutionEntity),
            AnnotationType.Unit => typeof(UnitEntity),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }

    public static (Type Entity, Type Annotation) GetTypesPair(AnnotationType type)
    {
        return type switch
        {
            AnnotationType.Responsibility => (typeof(ResponsibilityEntity), typeof(Responsibility)),
            AnnotationType.Subject => (typeof(SubjectEntity), typeof(Subject)),
            AnnotationType.Usage => (typeof(UsageEntity), typeof(Usage)),
            AnnotationType.Context => (typeof(ContextEntity), typeof(Context)),
            AnnotationType.Execution => (typeof(ExecutionEntity), typeof(Execution)),
            AnnotationType.Unit => (typeof(UnitEntity), typeof(Unit)),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }

    public static AnnotationType GetAnnotationType(Type type)
    {
        return type switch
        {
            { } when type == typeof(ResponsibilityEntity) => AnnotationType.Responsibility,
            { } when type == typeof(SubjectEntity) => AnnotationType.Subject,
            { } when type == typeof(UsageEntity) => AnnotationType.Usage,
            { } when type == typeof(ContextEntity) => AnnotationType.Context,
            { } when type == typeof(ExecutionEntity) => AnnotationType.Execution,
            { } when type == typeof(UnitEntity) => AnnotationType.Unit,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }
}
