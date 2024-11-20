using System.Collections.Generic;
using System.Linq;
using _42.Platform.Storyteller.Entities.Access;
using _42.Platform.Storyteller.Entities.Annotations;

namespace _42.Platform.Storyteller;

public static class Create
{
    public static ResponsibilityEntity Responsibility(
        string responsibilityName,
        string viewName = Constants.DefaultViewName,
        string projectName = Constants.DefaultProjectName)
    {
        return new ResponsibilityEntity
        {
            Id = string.Empty,
            PartitionKey = PartitionKeys.GetResponsibility(projectName, responsibilityName),
            ProjectName = projectName,
            ViewName = viewName,
            AnnotationType = AnnotationType.Responsibility,
            AnnotationKey = $"{AnnotationTypeCodes.Responsibility}.{responsibilityName}",
            Name = responsibilityName,
        };
    }

    public static SubjectEntity Subject(
        string subjectName,
        IEnumerable<string> responsibilityNames,
        IEnumerable<string> contextNames,
        string viewName = Constants.DefaultViewName,
        string projectName = Constants.DefaultProjectName)
    {
        return new SubjectEntity
        {
            Id = string.Empty,
            PartitionKey = PartitionKeys.GetSubject(projectName, subjectName),
            ProjectName = projectName,
            ViewName = viewName,
            AnnotationType = AnnotationType.Subject,
            AnnotationKey = $"{AnnotationTypeCodes.Subject}.{subjectName}",
            Name = subjectName,
            Usages = responsibilityNames.ToList(),
            Contexts = contextNames.ToList(),
        };
    }

    public static ContextEntity Context(
        string subjectName,
        string contextName,
        IEnumerable<string> responsibilityNames,
        string viewName = Constants.DefaultViewName,
        string projectName = Constants.DefaultProjectName)
    {
        return new ContextEntity
        {
            Id = string.Empty,
            PartitionKey = PartitionKeys.GetSubject(projectName, subjectName),
            ProjectName = projectName,
            ViewName = viewName,
            AnnotationType = AnnotationType.Context,
            AnnotationKey = $"{AnnotationTypeCodes.Context}.{subjectName}.{contextName}",
            Name = contextName,
            SubjectKey = $"{AnnotationTypeCodes.Subject}.{subjectName}",
            SubjectName = subjectName,
            Executions = responsibilityNames.ToList(),
        };
    }

    public static UsageEntity Usage(
        string subjectName,
        string responsibilityName,
        IEnumerable<string> contextNames,
        string viewName = Constants.DefaultViewName,
        string projectName = Constants.DefaultProjectName)
    {
        return new UsageEntity
        {
            Id = string.Empty,
            PartitionKey = PartitionKeys.GetResponsibility(projectName, responsibilityName),
            ProjectName = projectName,
            ViewName = viewName,
            AnnotationType = AnnotationType.Usage,
            AnnotationKey = $"{AnnotationTypeCodes.Usage}.{subjectName}.{responsibilityName}",
            Name = responsibilityName,
            ResponsibilityKey = $"{AnnotationTypeCodes.Responsibility}.{responsibilityName}",
            SubjectKey = $"{AnnotationTypeCodes.Subject}.{subjectName}",
            ResponsibilityName = responsibilityName,
            SubjectName = subjectName,
            Executions = contextNames.ToList(),
        };
    }

    public static ExecutionEntity Execution(
        string subjectName,
        string responsibilityName,
        string contextName,
        string viewName = Constants.DefaultViewName,
        string projectName = Constants.DefaultProjectName)
    {
        return new ExecutionEntity
        {
            Id = string.Empty,
            PartitionKey = PartitionKeys.GetResponsibility(projectName, responsibilityName),
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
        };
    }

    public static AccountEntity BaseAccount(
        string organizationName,
        string accountId,
        string accountUserName,
        string accountName)
    {
        return new AccountEntity
        {
            Id = accountId,
            UserName = accountUserName,
            Name = accountName,
            AccessMap = new Dictionary<string, AccountRole>
            {
                { organizationName, AccountRole.Owner },
                { $"{organizationName}.{Constants.DefaultProjectName}", AccountRole.Owner },
            },
        };
    }

    public static AccessPointEntity[] BaseAccessPoints(
        string organizationName,
        string userId,
        string projectName = Constants.DefaultProjectName)
    {
        return new[]
        {
            new AccessPointEntity
            {
                Key = organizationName,
                AccessMap = new Dictionary<string, AccountRole>
                {
                    { userId, AccountRole.Owner },
                },
            },
            new AccessPointEntity
            {
                Key = $"{organizationName}.{projectName}",
                AccessMap = new Dictionary<string, AccountRole>
                {
                    { userId, AccountRole.Owner },
                },
            },
        };
    }
}
