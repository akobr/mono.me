using System;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller;

public static class PartitionKeys
{
    public static string GetResponsibility(string projectName, string responsibilityName)
    {
        return $"{projectName}.{AnnotationTypeCodes.Responsibility}.{responsibilityName}";
    }

    public static string GetSubject(string projectName, string subjectName)
    {
        return $"{projectName}.{AnnotationTypeCodes.Subject}.{subjectName}";
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
            _ => throw new NotSupportedException($"Unknown annotation type: {annotation.GetType().Name}"),
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
}
