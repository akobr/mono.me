using System;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller;

public static class PartitionKeys
{
    public const string TemplatePartitionSuffix = "template";
    public const string KeyValuesPartitionInfix = "kv";
    public const string TransferPartitionSuffix = "transfer";

    public static string GetResponsibility(string projectName, string responsibilityName)
    {
        return $"{projectName}.{AnnotationTypeCodes.Responsibility}.{responsibilityName}";
    }

    public static string GetSubject(string projectName, string subjectName)
    {
        return $"{projectName}.{AnnotationTypeCodes.Subject}.{subjectName}";
    }

    public static string GetTemplate(string projectName)
    {
        return $"{projectName}.{TemplatePartitionSuffix}";
    }

    // Key/value pairs of one view; an annotation partition always has ".rst." or ".sbt." after the project, so they never collide.
    public static string GetKeyValues(string projectName, string viewName)
    {
        return $"{projectName}.{KeyValuesPartitionInfix}.{viewName}";
    }

    // Export and import jobs of all views of a project.
    public static string GetTransfer(string projectName)
    {
        return $"{projectName}.{TransferPartitionSuffix}";
    }

    public static string GetKey(IAnnotation annotation)
    {
        return annotation switch
        {
            IResponsibility responsibility => GetResponsibility(responsibility.ProjectName, responsibility.Name),
            IUsage usage => GetResponsibility(usage.ProjectName, usage.ResponsibilityName),
            IExecution execution => GetResponsibility(execution.ProjectName, execution.ResponsibilityName),
            ISubject subject => GetSubject(subject.ProjectName, subject.Name),
            IContext context => GetSubject(context.ProjectName, context.SubjectName),
            IUnit unit => GetResponsibility(unit.ProjectName, unit.ResponsibilityName),
            IUnitOfExecution unitOfExecution => GetResponsibility(unitOfExecution.ProjectName, unitOfExecution.ResponsibilityName),
            _ => throw new NotSupportedException($"Unknown annotation type: {annotation.GetType().Name}"),
        };
    }

    public static string GetKey(AnnotationKey annotationKey, string projectName)
    {
        return annotationKey.Type switch
        {
            AnnotationType.Responsibility or AnnotationType.Unit or AnnotationType.Usage or AnnotationType.Execution or AnnotationType.UnitOfExecution => GetResponsibility(projectName, annotationKey.ResponsibilityName),
            AnnotationType.Subject or AnnotationType.Context => GetSubject(projectName, annotationKey.SubjectName),
            _ => throw new NotSupportedException($"Unknown annotation type: {annotationKey.Type}"),
        };
    }

    public static PartitionKey GetCosmosKey(IAnnotation annotation)
    {
        return new PartitionKey(GetKey(annotation));
    }

    public static PartitionKey GetCosmosResponsibility(string projectName, string responsibilityName)
    {
        return new PartitionKey(GetResponsibility(projectName, responsibilityName));
    }

    public static PartitionKey GetCosmosSubject(string projectName, string subjectName)
    {
        return new PartitionKey(GetSubject(projectName, subjectName));
    }

    public static PartitionKey GetCosmosTemplate(string projectName)
    {
        return new PartitionKey(GetTemplate(projectName));
    }

    public static PartitionKey GetCosmosKeyValues(string projectName, string viewName)
    {
        return new PartitionKey(GetKeyValues(projectName, viewName));
    }

    public static PartitionKey GetCosmosTransfer(string projectName)
    {
        return new PartitionKey(GetTransfer(projectName));
    }
}
