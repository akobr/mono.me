using System;

namespace _42.Platform.Storyteller;

public static class AnnotationTypes
{
    public static AnnotationType GetType(string code)
    {
        if (!AnnotationTypeCodes.ValidCodes.TryGetValue(code, out var type))
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        return type;
    }

    public static Type GetRuntimeType(AnnotationType type)
    {
        return type switch
        {
            AnnotationType.Responsibility => typeof(Responsibility),
            AnnotationType.Subject => typeof(Subject),
            AnnotationType.Usage => typeof(Usage),
            AnnotationType.Context => typeof(Context),
            AnnotationType.Execution => typeof(Execution),
            AnnotationType.Unit => typeof(Unit),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }

    public static AnnotationType GetType(Type runtimeType)
    {
        return runtimeType switch
        {
            { } when runtimeType == typeof(Responsibility) => AnnotationType.Responsibility,
            { } when runtimeType == typeof(Subject) => AnnotationType.Subject,
            { } when runtimeType == typeof(Usage) => AnnotationType.Usage,
            { } when runtimeType == typeof(Context) => AnnotationType.Context,
            { } when runtimeType == typeof(Execution) => AnnotationType.Execution,
            { } when runtimeType == typeof(Unit) => AnnotationType.Unit,
            _ => throw new ArgumentOutOfRangeException(nameof(runtimeType), runtimeType, null),
        };
    }
}
