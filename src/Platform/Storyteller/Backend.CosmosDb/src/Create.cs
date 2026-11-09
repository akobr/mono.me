using System;
using System.Collections.Generic;
using System.Linq;
using _42.Platform.Storyteller.Entities.Access;
using _42.Platform.Storyteller.Entities.Annotations;
using _42.Platform.Storyteller.Entities.Configurations;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller;

// The seeded items of an organization; each carries the organization of its partition in the container organizations.
public static class Create
{
    public static ResponsibilityEntity Responsibility(
        string organization,
        string responsibilityName,
        IEnumerable<string>? unitNames = null,
        string viewName = Constants.DefaultViewName,
        string projectName = Constants.DefaultProjectName)
    {
        var stamp = SystemStamp();

        return new ResponsibilityEntity
        {
            Id = string.Empty,
            PartitionKey = PartitionKeys.GetResponsibility(projectName, responsibilityName),
            Organization = organization,
            ProjectName = projectName,
            ViewName = viewName,
            AnnotationType = AnnotationType.Responsibility,
            Created = stamp,
            Modified = stamp,
            AnnotationKey = $"{AnnotationTypeCodes.Responsibility}.{responsibilityName}",
            Name = responsibilityName,
            UnitNames = unitNames?.ToList() ?? [],
        };
    }

    public static SubjectEntity Subject(
        string organization,
        string subjectName,
        IEnumerable<string>? responsibilityNames = null,
        IEnumerable<string>? contextNames = null,
        string viewName = Constants.DefaultViewName,
        string projectName = Constants.DefaultProjectName)
    {
        var stamp = SystemStamp();

        return new SubjectEntity
        {
            Id = string.Empty,
            PartitionKey = PartitionKeys.GetSubject(projectName, subjectName),
            Organization = organization,
            ProjectName = projectName,
            ViewName = viewName,
            AnnotationType = AnnotationType.Subject,
            Created = stamp,
            Modified = stamp,
            AnnotationKey = $"{AnnotationTypeCodes.Subject}.{subjectName}",
            Name = subjectName,
            ResponsibilityNames = responsibilityNames?.ToList() ?? [],
            ContextNames = contextNames?.ToList() ?? [],
        };
    }

    public static ContextEntity Context(
        string organization,
        string subjectName,
        string contextName,
        IEnumerable<string>? responsibilityNames = null,
        string viewName = Constants.DefaultViewName,
        string projectName = Constants.DefaultProjectName)
    {
        var stamp = SystemStamp();

        return new ContextEntity
        {
            Id = string.Empty,
            PartitionKey = PartitionKeys.GetSubject(projectName, subjectName),
            Organization = organization,
            ProjectName = projectName,
            ViewName = viewName,
            AnnotationType = AnnotationType.Context,
            Created = stamp,
            Modified = stamp,
            AnnotationKey = $"{AnnotationTypeCodes.Context}.{subjectName}.{contextName}",
            Name = contextName,
            SubjectKey = $"{AnnotationTypeCodes.Subject}.{subjectName}",
            SubjectName = subjectName,
            ResponsibilityNames = responsibilityNames?.ToList() ?? [],
        };
    }

    public static UsageEntity Usage(
        string organization,
        string subjectName,
        string responsibilityName,
        IEnumerable<string>? contextNames = null,
        string viewName = Constants.DefaultViewName,
        string projectName = Constants.DefaultProjectName)
    {
        var stamp = SystemStamp();

        return new UsageEntity
        {
            Id = string.Empty,
            PartitionKey = PartitionKeys.GetResponsibility(projectName, responsibilityName),
            Organization = organization,
            ProjectName = projectName,
            ViewName = viewName,
            AnnotationType = AnnotationType.Usage,
            Created = stamp,
            Modified = stamp,
            AnnotationKey = $"{AnnotationTypeCodes.Usage}.{subjectName}.{responsibilityName}",
            Name = responsibilityName,
            ResponsibilityKey = $"{AnnotationTypeCodes.Responsibility}.{responsibilityName}",
            SubjectKey = $"{AnnotationTypeCodes.Subject}.{subjectName}",
            ResponsibilityName = responsibilityName,
            SubjectName = subjectName,
            ContextNames = contextNames?.ToList() ?? [],
        };
    }

    public static ExecutionEntity Execution(
        string organization,
        string subjectName,
        string responsibilityName,
        string contextName,
        IEnumerable<string>? unitNames = null,
        string viewName = Constants.DefaultViewName,
        string projectName = Constants.DefaultProjectName)
    {
        var stamp = SystemStamp();

        return new ExecutionEntity
        {
            Id = string.Empty,
            PartitionKey = PartitionKeys.GetResponsibility(projectName, responsibilityName),
            Organization = organization,
            ProjectName = projectName,
            ViewName = viewName,
            AnnotationType = AnnotationType.Execution,
            Created = stamp,
            Modified = stamp,
            AnnotationKey = $"{AnnotationTypeCodes.Execution}.{subjectName}.{responsibilityName}.{contextName}",
            Name = contextName,
            ResponsibilityKey = $"{AnnotationTypeCodes.Responsibility}.{responsibilityName}",
            SubjectKey = $"{AnnotationTypeCodes.Subject}.{subjectName}",
            ContextKey = $"{AnnotationTypeCodes.Context}.{subjectName}.{contextName}",
            UsageKey = $"{AnnotationTypeCodes.Usage}.{subjectName}.{responsibilityName}",
            ResponsibilityName = responsibilityName,
            SubjectName = subjectName,
            ContextName = contextName,
            UnitNames = unitNames?.ToList() ?? [],
        };
    }

    public static UnitOfExecutionEntity UnitOfExecution(
        string organization,
        string subjectName,
        string responsibilityName,
        string unitName,
        string contextName,
        string viewName = Constants.DefaultViewName,
        string projectName = Constants.DefaultProjectName)
    {
        var stamp = SystemStamp();

        return new UnitOfExecutionEntity
        {
            Id = string.Empty,
            PartitionKey = PartitionKeys.GetResponsibility(projectName, responsibilityName),
            Organization = organization,
            ProjectName = projectName,
            ViewName = viewName,
            AnnotationType = AnnotationType.UnitOfExecution,
            Created = stamp,
            Modified = stamp,
            AnnotationKey = $"{AnnotationTypeCodes.UnitOfExecution}.{subjectName}.{responsibilityName}.{contextName}.{unitName}",
            Name = unitName,
            ResponsibilityKey = $"{AnnotationTypeCodes.Responsibility}.{responsibilityName}",
            UnitKey = $"{AnnotationTypeCodes.Unit}.{responsibilityName}.{unitName}",
            SubjectKey = $"{AnnotationTypeCodes.Subject}.{subjectName}",
            ContextKey = $"{AnnotationTypeCodes.Context}.{subjectName}.{contextName}",
            UsageKey = $"{AnnotationTypeCodes.Usage}.{subjectName}.{responsibilityName}",
            ExecutionKey = $"{AnnotationTypeCodes.Execution}.{subjectName}.{responsibilityName}.{contextName}",
            ResponsibilityName = responsibilityName,
            SubjectName = subjectName,
            ContextName = contextName,
            UnitName = unitName,
        };
    }

    public static ConfigurationEntity Configuration(string organization, AnnotationKey annotationKey, JObject content, AuditActor? author = null, string viewName = Constants.DefaultViewName, string projectName = Constants.DefaultProjectName)
    {
        return new ConfigurationEntity
        {
            Id = $"{viewName}.cnf.{annotationKey}",
            PartitionKey = PartitionKeys.GetKey(annotationKey, projectName),
            Organization = organization,
            AnnotationKey = annotationKey,
            Name = annotationKey.Name,
            Content = content,
            Version = 1,
            Modified = AuditStamp.Of(author ?? AuditActor.System, DateTimeOffset.UtcNow),
            ViewName = viewName,
            ProjectName = projectName,
        };
    }

    // The seed is written by the system; like every stored annotation it carries both stamps.
    private static AuditStamp SystemStamp()
    {
        return new AuditStamp { At = DateTimeOffset.UtcNow, By = AuditActor.System };
    }
}
