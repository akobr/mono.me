using System;

namespace _42.Platform.Storyteller;

public static class AnnotationKeyExtensions
{
    public static bool IsValid(this AnnotationKey @this)
    {
        return @this.Type switch
        {
            AnnotationType.Responsibility => @this.Segments.Count == 2,
            AnnotationType.Subject => @this.Segments.Count == 2,
            AnnotationType.Usage => @this.Segments.Count == 3,
            AnnotationType.Context => @this.Segments.Count == 3,
            AnnotationType.Execution => @this.Segments.Count == 4,
            AnnotationType.Unit => @this.Segments.Count == 3,
            _ => false,
        };
    }

    public static string GetSubjectName(this AnnotationKey @this)
    {
        if (@this.Type < AnnotationType.Subject)
        {
            throw new ArgumentException("Annotation which is not subject specific.", nameof(@this));
        }

        return @this.SubjectName;
    }

    public static AnnotationKey GetSubjectKey(this AnnotationKey @this)
    {
        return AnnotationKey.CreateSubject(GetSubjectName(@this));
    }

    public static string GetResponsibilityName(this AnnotationKey @this)
    {
        return @this.Type switch
        {
            AnnotationType.Responsibility or AnnotationType.Unit or AnnotationType.Usage or AnnotationType.Execution => @this.ResponsibilityName,
            _ => throw new ArgumentException("Annotation which is not responsibility specific.", nameof(@this)),
        };
    }

    public static AnnotationKey GetResponsibilityKey(this AnnotationKey @this)
    {
        return AnnotationKey.CreateResponsibility(GetResponsibilityName(@this));
    }

    public static string GetContextName(this AnnotationKey @this)
    {
        return @this.Type switch
        {
            AnnotationType.Context or AnnotationType.Execution => @this.ContextName,
            _ => throw new ArgumentException("Annotation which is not context specific.", nameof(@this)),
        };
    }

    public static AnnotationKey GetContextKey(this AnnotationKey @this)
    {
        return AnnotationKey.CreateContext(GetSubjectName(@this), GetContextName(@this));
    }

    public static AnnotationKey GetUsageKey(this AnnotationKey @this)
    {
        return AnnotationKey.CreateUsage(GetSubjectName(@this), GetResponsibilityName(@this));
    }
}
