using System;
using System.Collections.Generic;
using System.Linq;
using _42.Platform.Storyteller.Entities.Annotations;

namespace _42.Platform.Storyteller.Annotating;

// The denormalized arrays of the parents that hold the names of their active children.
internal static class AnnotationRelations
{
    public const string UnitNames = nameof(ResponsibilityEntity.UnitNames);
    public const string ResponsibilityNames = nameof(SubjectEntity.ResponsibilityNames);
    public const string ContextNames = nameof(SubjectEntity.ContextNames);

    // The array of each direct parent that lists the child, with the name it lists.
    public static IReadOnlyList<(AnnotationKey Parent, string Array, string Value)> GetParentRelations(AnnotationKey child)
    {
        return child.Type switch
        {
            AnnotationType.Unit => [(child.GetResponsibilityKey(), UnitNames, child.UnitName)],
            AnnotationType.Usage => [(child.GetSubjectKey(), ResponsibilityNames, child.ResponsibilityName)],
            AnnotationType.Context => [(child.GetSubjectKey(), ContextNames, child.ContextName)],
            AnnotationType.Execution =>
            [
                (child.GetUsageKey(), ContextNames, child.ContextName),
                (child.GetContextKey(), ResponsibilityNames, child.ResponsibilityName),
            ],
            AnnotationType.UnitOfExecution => [(child.GetExecutionKey(), UnitNames, child.UnitName)],
            _ => [],
        };
    }

    public static AnnotationEntity Add(AnnotationEntity entity, string array, string value)
    {
        var values = Get(entity, array);
        return values.Contains(value, StringComparer.Ordinal)
            ? entity
            : With(entity, array, values.Append(value).ToList());
    }

    public static AnnotationEntity Remove(AnnotationEntity entity, string array, string value)
    {
        var values = Get(entity, array);
        return values.Contains(value, StringComparer.Ordinal)
            ? With(entity, array, values.Where(existing => !string.Equals(existing, value, StringComparison.Ordinal)).ToList())
            : entity;
    }

    // The server owns the arrays: an update keeps the stored ones whatever the body says.
    public static AnnotationEntity CopyFrom(AnnotationEntity target, AnnotationEntity source)
    {
        return (target, source) switch
        {
            (ResponsibilityEntity t, ResponsibilityEntity s) => t with { UnitNames = s.UnitNames },
            (SubjectEntity t, SubjectEntity s) => t with { ResponsibilityNames = s.ResponsibilityNames, ContextNames = s.ContextNames },
            (UsageEntity t, UsageEntity s) => t with { ContextNames = s.ContextNames },
            (ContextEntity t, ContextEntity s) => t with { ResponsibilityNames = s.ResponsibilityNames },
            (ExecutionEntity t, ExecutionEntity s) => t with { UnitNames = s.UnitNames },
            _ => target,
        };
    }

    public static IReadOnlyCollection<string> Get(AnnotationEntity entity, string array)
    {
        return (entity, array) switch
        {
            (ResponsibilityEntity e, UnitNames) => e.UnitNames,
            (SubjectEntity e, ResponsibilityNames) => e.ResponsibilityNames,
            (SubjectEntity e, ContextNames) => e.ContextNames,
            (UsageEntity e, ContextNames) => e.ContextNames,
            (ContextEntity e, ResponsibilityNames) => e.ResponsibilityNames,
            (ExecutionEntity e, UnitNames) => e.UnitNames,
            _ => throw new ArgumentOutOfRangeException(nameof(array), array, $"{entity.AnnotationType} has no array {array}."),
        };
    }

    private static AnnotationEntity With(AnnotationEntity entity, string array, IReadOnlyCollection<string> values)
    {
        return (entity, array) switch
        {
            (ResponsibilityEntity e, UnitNames) => e with { UnitNames = values },
            (SubjectEntity e, ResponsibilityNames) => e with { ResponsibilityNames = values },
            (SubjectEntity e, ContextNames) => e with { ContextNames = values },
            (UsageEntity e, ContextNames) => e with { ContextNames = values },
            (ContextEntity e, ResponsibilityNames) => e with { ResponsibilityNames = values },
            (ExecutionEntity e, UnitNames) => e with { UnitNames = values },
            _ => throw new ArgumentOutOfRangeException(nameof(array), array, $"{entity.AnnotationType} has no array {array}."),
        };
    }
}
