using System;

namespace _42.Platform.Storyteller;

public static class AnnotationKeyExtensions
{
    public static bool IsValid(this AnnotationKey @this)
    {
        return @this.Type switch
        {
            AnnotationType.Responsibility or AnnotationType.Subject => @this.Segments.Count == 2,
            AnnotationType.Usage or AnnotationType.Context or AnnotationType.Unit => @this.Segments.Count == 3,
            AnnotationType.Execution => @this.Segments.Count == 4,
            AnnotationType.UnitOfExecution => @this.Segments.Count == 5,
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
        return AnnotationKey.CreateSubject(@this.GetSubjectName());
    }

    public static string GetResponsibilityName(this AnnotationKey @this)
    {
        return @this.Type switch
        {
            AnnotationType.Responsibility or AnnotationType.Unit or AnnotationType.Usage or AnnotationType.Execution or AnnotationType.UnitOfExecution => @this.ResponsibilityName,
            _ => throw new ArgumentException("Annotation which is not responsibility specific.", nameof(@this)),
        };
    }

    public static AnnotationKey GetResponsibilityKey(this AnnotationKey @this)
    {
        return AnnotationKey.CreateResponsibility(@this.GetResponsibilityName());
    }

    public static string GetContextName(this AnnotationKey @this)
    {
        return @this.Type switch
        {
            AnnotationType.Context or AnnotationType.Execution or AnnotationType.UnitOfExecution => @this.ContextName,
            _ => throw new ArgumentException("Annotation which is not context specific.", nameof(@this)),
        };
    }

    public static AnnotationKey GetContextKey(this AnnotationKey @this)
    {
        return AnnotationKey.CreateContext(@this.GetSubjectName(), @this.GetContextName());
    }

    public static AnnotationKey GetUsageKey(this AnnotationKey @this)
    {
        return AnnotationKey.CreateUsage(@this.GetSubjectName(), @this.GetResponsibilityName());
    }

    public static string GetUnitName(this AnnotationKey @this)
    {
        return @this.Type switch
        {
            AnnotationType.Unit or AnnotationType.UnitOfExecution => @this.UnitName,
            _ => throw new ArgumentException("Annotation which is not unit specific.", nameof(@this)),
        };
    }

    public static AnnotationKey GetUnitKey(this AnnotationKey @this)
    {
        return AnnotationKey.CreateUnit(@this.GetResponsibilityName(), @this.GetUnitName());
    }

    public static AnnotationKey GetExecutionKey(this AnnotationKey @this)
    {
        return AnnotationKey.CreateExecution(@this.GetSubjectName(), @this.GetResponsibilityName(), @this.GetContextName());
    }
}
