using System;
using System.Collections.Generic;
using System.Linq;
using _42.Platform.Storyteller.Entities;

namespace _42.Platform.Storyteller;

public static class Create
{
    public static Responsibility Responsibility(
        string responsibilityName,
        string viewName = Constants.DefaultViewName,
        string projectName = Constants.DefaultProjectName)
    {
        return new Responsibility
        {
            PartitionKey = $"{projectName}.{AnnotationTypeCodes.Responsibility}.{responsibilityName}",
            ProjectName = projectName,
            ViewName = viewName,
            AnnotationType = AnnotationType.Responsibility,
            AnnotationKey = $"{AnnotationTypeCodes.Responsibility}.{responsibilityName}",
            Name = responsibilityName,
            SystemId = Guid.NewGuid(),
            SystemPublicKey = Guid.NewGuid().ToString("N"),
        };
    }

    public static Subject Subject(
        string subjectName,
        IEnumerable<string> responsibilityNames,
        IEnumerable<string> contextNames,
        string viewName = Constants.DefaultViewName,
        string projectName = Constants.DefaultProjectName)
    {
        return new Subject
        {
            PartitionKey = $"{projectName}.{AnnotationTypeCodes.Subject}.{subjectName}",
            ProjectName = projectName,
            ViewName = viewName,
            AnnotationType = AnnotationType.Subject,
            AnnotationKey = $"{AnnotationTypeCodes.Subject}.{subjectName}",
            Name = subjectName,
            Usages = responsibilityNames
                .Select(name => AnnotationKey.CreateUsage(subjectName, name).ToString())
                .ToList(),
            Contexts = contextNames
                .Select(name => AnnotationKey.CreateContext(subjectName, name).ToString())
                .ToList(),
            SystemId = Guid.NewGuid(),
            SystemPublicKey = Guid.NewGuid().ToString("N"),
        };
    }

    public static Context Context(
        string subjectName,
        string contextName,
        string viewName = Constants.DefaultViewName,
        string projectName = Constants.DefaultProjectName)
    {
        return new Context
        {
            PartitionKey = $"{projectName}.{AnnotationTypeCodes.Subject}.{subjectName}",
            ProjectName = projectName,
            ViewName = viewName,
            AnnotationType = AnnotationType.Context,
            AnnotationKey = $"{AnnotationTypeCodes.Context}.{subjectName}.{contextName}",
            Name = contextName,
            SubjectKey = $"{AnnotationTypeCodes.Subject}.{subjectName}",
            SubjectName = subjectName,
            SystemId = Guid.NewGuid(),
            SystemPublicKey = Guid.NewGuid().ToString("N"),
        };
    }

    public static Usage Usage(
        string subjectName,
        string responsibilityName,
        IEnumerable<string> contextNames,
        string viewName = Constants.DefaultViewName,
        string projectName = Constants.DefaultProjectName)
    {
        return new Usage
        {
            PartitionKey = $"{projectName}.{AnnotationTypeCodes.Responsibility}.{responsibilityName}",
            ProjectName = projectName,
            ViewName = viewName,
            AnnotationType = AnnotationType.Usage,
            AnnotationKey = $"{AnnotationTypeCodes.Usage}.{subjectName}.{responsibilityName}",
            Name = responsibilityName,
            ResponsibilityKey = $"{AnnotationTypeCodes.Responsibility}.{responsibilityName}",
            SubjectKey = $"{AnnotationTypeCodes.Subject}.{subjectName}",
            ResponsibilityName = responsibilityName,
            SubjectName = subjectName,
            Executions = contextNames
                .Select(name => AnnotationKey.CreateExecution(subjectName, responsibilityName, name).ToString())
                .ToList(),
            SystemId = Guid.NewGuid(),
            SystemPublicKey = Guid.NewGuid().ToString("N"),
        };
    }

    public static Execution Execution(
        string subjectName,
        string responsibilityName,
        string contextName,
        string viewName = Constants.DefaultViewName,
        string projectName = Constants.DefaultProjectName)
    {
        return new Execution
        {
            PartitionKey = $"{projectName}.{AnnotationTypeCodes.Responsibility}.{responsibilityName}",
            ProjectName = projectName,
            ViewName = viewName,
            AnnotationType = AnnotationType.Execution,
            AnnotationKey = $"{AnnotationTypeCodes.Execution}.{subjectName}.{responsibilityName}.{contextName}",
            Name = contextName,
            ResponsibilityKey = $"{AnnotationTypeCodes.Responsibility}.{responsibilityName}",
            SubjectKey = $"{AnnotationTypeCodes.Subject}.{subjectName}",
            ContextKey = $"{AnnotationTypeCodes.Context}.{subjectName}.{contextName}",
            ResponsibilityName = responsibilityName,
            SubjectName = subjectName,
            ContextName = contextName,
            SystemId = Guid.NewGuid(),
            SystemPublicKey = Guid.NewGuid().ToString("N"),
        };
    }
}
