using System;
using _42.Platform.Storyteller.Entities;

namespace _42.Platform.Storyteller;

public static class AnnotationTypeMap
{
    public static Type GetSystemType(AnnotationType annotationType)
    {
        return annotationType switch
        {
            AnnotationType.Responsibility => typeof(Responsibility),
            // AnnotationType.Job => typeof(Job),
            AnnotationType.Subject => typeof(Subject),
            AnnotationType.Usage => typeof(Usage),
            AnnotationType.Context => typeof(Context),
            AnnotationType.Execution => typeof(Execution),
            _ => throw new ArgumentOutOfRangeException(nameof(annotationType), annotationType, null),
        };
    }

    public static AnnotationType GetAnnotationType(Type type)
    {
        return type switch
        {
            { } when type == typeof(Responsibility) => AnnotationType.Responsibility,
            // { } t when t == typeof(Job) => AnnotationType.Job,
            { } when type == typeof(Subject) => AnnotationType.Subject,
            { } when type == typeof(Usage) => AnnotationType.Usage,
            { } when type == typeof(Context) => AnnotationType.Context,
            { } when type == typeof(Execution) => AnnotationType.Execution,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }
}
