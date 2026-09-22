using System;
using System.Collections.Generic;
using _42.Platform.Storyteller.Entities.Annotations;

namespace _42.Platform.Storyteller;

internal static class AnnotationMappings
{
    public static Annotation ToModel(this AnnotationEntity entity)
    {
        return entity switch
        {
            ResponsibilityEntity e => e.ToResponsibility(),
            SubjectEntity e => e.ToSubject(),
            UsageEntity e => e.ToUsage(),
            ContextEntity e => e.ToContext(),
            ExecutionEntity e => e.ToExecution(),
            UnitEntity e => e.ToUnit(),
            UnitOfExecutionEntity e => e.ToUnitOfExecution(),
            _ => throw new NotSupportedException($"Unsupported annotation entity type: {entity.GetType().Name}"),
        };
    }

    public static TAnnotation ToModel<TEntity, TAnnotation>(this TEntity entity)
        where TEntity : AnnotationEntity
        where TAnnotation : Annotation
        => (TAnnotation)((AnnotationEntity)entity).ToModel();

    public static AnnotationEntity ToEntity(this Annotation annotation)
    {
        return annotation switch
        {
            Responsibility m => m.ToResponsibilityEntity(),
            Subject m => m.ToSubjectEntity(),
            Usage m => m.ToUsageEntity(),
            Context m => m.ToContextEntity(),
            Execution m => m.ToExecutionEntity(),
            Unit m => m.ToUnitEntity(),
            UnitOfExecution m => m.ToUnitOfExecutionEntity(),
            _ => throw new NotSupportedException($"Unsupported annotation type: {annotation.GetType().Name}"),
        };
    }

    private static Responsibility ToResponsibility(this ResponsibilityEntity e) => new()
    {
        ProjectName = e.ProjectName,
        ViewName = e.ViewName,
        AnnotationKey = e.AnnotationKey,
        Name = e.Name,
        AnnotationType = e.AnnotationType,
        IsDisabled = e.IsDisabled,
        ValidFrom = e.ValidFrom,
        ExpiresAt = e.ExpiresAt,
        TimeZone = e.TimeZone,
        Title = e.Title,
        Description = e.Description,
        DocumentationLink = e.DocumentationLink,
        Labels = e.Labels,
        Values = e.Values,
        UnitNames = new HashSet<string>(e.UnitNames),
    };

    private static ResponsibilityEntity ToResponsibilityEntity(this Responsibility m) => new()
    {
        Id = string.Empty,
        PartitionKey = string.Empty,
        ProjectName = m.ProjectName,
        ViewName = m.ViewName,
        AnnotationKey = m.AnnotationKey,
        Name = m.Name,
        AnnotationType = m.AnnotationType,
        IsDisabled = m.IsDisabled,
        ValidFrom = m.ValidFrom,
        ExpiresAt = m.ExpiresAt,
        TimeZone = m.TimeZone,
        Title = m.Title,
        Description = m.Description,
        DocumentationLink = m.DocumentationLink,
        Labels = m.Labels,
        Values = m.Values,
        UnitNames = m.UnitNames,
    };

    private static Subject ToSubject(this SubjectEntity e) => new()
    {
        ProjectName = e.ProjectName,
        ViewName = e.ViewName,
        AnnotationKey = e.AnnotationKey,
        Name = e.Name,
        AnnotationType = e.AnnotationType,
        IsDisabled = e.IsDisabled,
        ValidFrom = e.ValidFrom,
        ExpiresAt = e.ExpiresAt,
        TimeZone = e.TimeZone,
        Title = e.Title,
        Description = e.Description,
        DocumentationLink = e.DocumentationLink,
        Labels = e.Labels,
        Values = e.Values,
        ContextNames = new HashSet<string>(e.ContextNames),
        ResponsibilityNames = new HashSet<string>(e.ResponsibilityNames),
    };

    private static SubjectEntity ToSubjectEntity(this Subject m) => new()
    {
        Id = string.Empty,
        PartitionKey = string.Empty,
        ProjectName = m.ProjectName,
        ViewName = m.ViewName,
        AnnotationKey = m.AnnotationKey,
        Name = m.Name,
        AnnotationType = m.AnnotationType,
        IsDisabled = m.IsDisabled,
        ValidFrom = m.ValidFrom,
        ExpiresAt = m.ExpiresAt,
        TimeZone = m.TimeZone,
        Title = m.Title,
        Description = m.Description,
        DocumentationLink = m.DocumentationLink,
        Labels = m.Labels,
        Values = m.Values,
        ContextNames = m.ContextNames,
        ResponsibilityNames = m.ResponsibilityNames,
    };

    private static Usage ToUsage(this UsageEntity e) => new()
    {
        ProjectName = e.ProjectName,
        ViewName = e.ViewName,
        AnnotationKey = e.AnnotationKey,
        Name = e.Name,
        AnnotationType = e.AnnotationType,
        IsDisabled = e.IsDisabled,
        ValidFrom = e.ValidFrom,
        ExpiresAt = e.ExpiresAt,
        TimeZone = e.TimeZone,
        Title = e.Title,
        Description = e.Description,
        DocumentationLink = e.DocumentationLink,
        Labels = e.Labels,
        Values = e.Values,
        SubjectKey = e.SubjectKey,
        ResponsibilityKey = e.ResponsibilityKey,
        SubjectName = e.SubjectName,
        ResponsibilityName = e.ResponsibilityName,
        ContextNames = new HashSet<string>(e.ContextNames),
    };

    private static UsageEntity ToUsageEntity(this Usage m) => new()
    {
        Id = string.Empty,
        PartitionKey = string.Empty,
        ProjectName = m.ProjectName,
        ViewName = m.ViewName,
        AnnotationKey = m.AnnotationKey,
        Name = m.Name,
        AnnotationType = m.AnnotationType,
        IsDisabled = m.IsDisabled,
        ValidFrom = m.ValidFrom,
        ExpiresAt = m.ExpiresAt,
        TimeZone = m.TimeZone,
        Title = m.Title,
        Description = m.Description,
        DocumentationLink = m.DocumentationLink,
        Labels = m.Labels,
        Values = m.Values,
        SubjectKey = m.SubjectKey,
        ResponsibilityKey = m.ResponsibilityKey,
        SubjectName = m.SubjectName,
        ResponsibilityName = m.ResponsibilityName,
        ContextNames = m.ContextNames,
    };

    private static Context ToContext(this ContextEntity e) => new()
    {
        ProjectName = e.ProjectName,
        ViewName = e.ViewName,
        AnnotationKey = e.AnnotationKey,
        Name = e.Name,
        AnnotationType = e.AnnotationType,
        IsDisabled = e.IsDisabled,
        ValidFrom = e.ValidFrom,
        ExpiresAt = e.ExpiresAt,
        TimeZone = e.TimeZone,
        Title = e.Title,
        Description = e.Description,
        DocumentationLink = e.DocumentationLink,
        Labels = e.Labels,
        Values = e.Values,
        SubjectKey = e.SubjectKey,
        SubjectName = e.SubjectName,
        ResponsibilityNames = new HashSet<string>(e.ResponsibilityNames),
    };

    private static ContextEntity ToContextEntity(this Context m) => new()
    {
        Id = string.Empty,
        PartitionKey = string.Empty,
        ProjectName = m.ProjectName,
        ViewName = m.ViewName,
        AnnotationKey = m.AnnotationKey,
        Name = m.Name,
        AnnotationType = m.AnnotationType,
        IsDisabled = m.IsDisabled,
        ValidFrom = m.ValidFrom,
        ExpiresAt = m.ExpiresAt,
        TimeZone = m.TimeZone,
        Title = m.Title,
        Description = m.Description,
        DocumentationLink = m.DocumentationLink,
        Labels = m.Labels,
        Values = m.Values,
        SubjectKey = m.SubjectKey,
        SubjectName = m.SubjectName,
        ResponsibilityNames = m.ResponsibilityNames,
    };

    private static Execution ToExecution(this ExecutionEntity e) => new()
    {
        ProjectName = e.ProjectName,
        ViewName = e.ViewName,
        AnnotationKey = e.AnnotationKey,
        Name = e.Name,
        AnnotationType = e.AnnotationType,
        IsDisabled = e.IsDisabled,
        ValidFrom = e.ValidFrom,
        ExpiresAt = e.ExpiresAt,
        TimeZone = e.TimeZone,
        Title = e.Title,
        Description = e.Description,
        DocumentationLink = e.DocumentationLink,
        Labels = e.Labels,
        Values = e.Values,
        ResponsibilityKey = e.ResponsibilityKey,
        SubjectKey = e.SubjectKey,
        ContextKey = e.ContextKey,
        UsageKey = e.UsageKey,
        SubjectName = e.SubjectName,
        ResponsibilityName = e.ResponsibilityName,
        ContextName = e.ContextName,
        UnitNames = new HashSet<string>(e.UnitNames),
    };

    private static ExecutionEntity ToExecutionEntity(this Execution m) => new()
    {
        Id = string.Empty,
        PartitionKey = string.Empty,
        ProjectName = m.ProjectName,
        ViewName = m.ViewName,
        AnnotationKey = m.AnnotationKey,
        Name = m.Name,
        AnnotationType = m.AnnotationType,
        IsDisabled = m.IsDisabled,
        ValidFrom = m.ValidFrom,
        ExpiresAt = m.ExpiresAt,
        TimeZone = m.TimeZone,
        Title = m.Title,
        Description = m.Description,
        DocumentationLink = m.DocumentationLink,
        Labels = m.Labels,
        Values = m.Values,
        ResponsibilityKey = m.ResponsibilityKey,
        SubjectKey = m.SubjectKey,
        ContextKey = m.ContextKey,
        UsageKey = m.UsageKey,
        SubjectName = m.SubjectName,
        ResponsibilityName = m.ResponsibilityName,
        ContextName = m.ContextName,
        UnitNames = m.UnitNames,
    };

    private static Unit ToUnit(this UnitEntity e) => new()
    {
        ProjectName = e.ProjectName,
        ViewName = e.ViewName,
        AnnotationKey = e.AnnotationKey,
        Name = e.Name,
        AnnotationType = e.AnnotationType,
        IsDisabled = e.IsDisabled,
        ValidFrom = e.ValidFrom,
        ExpiresAt = e.ExpiresAt,
        TimeZone = e.TimeZone,
        Title = e.Title,
        Description = e.Description,
        DocumentationLink = e.DocumentationLink,
        Labels = e.Labels,
        Values = e.Values,
        ResponsibilityKey = e.ResponsibilityKey,
        ResponsibilityName = e.ResponsibilityName,
        UnitType = e.UnitType,
        UnitDefinition = e.UnitDefinition,
    };

    private static UnitEntity ToUnitEntity(this Unit m) => new()
    {
        Id = string.Empty,
        PartitionKey = string.Empty,
        ProjectName = m.ProjectName,
        ViewName = m.ViewName,
        AnnotationKey = m.AnnotationKey,
        ResponsibilityKey = m.ResponsibilityKey,
        ResponsibilityName = m.ResponsibilityName,
        Name = m.Name,
        AnnotationType = m.AnnotationType,
        IsDisabled = m.IsDisabled,
        ValidFrom = m.ValidFrom,
        ExpiresAt = m.ExpiresAt,
        TimeZone = m.TimeZone,
        Title = m.Title,
        Description = m.Description,
        DocumentationLink = m.DocumentationLink,
        Labels = m.Labels,
        Values = m.Values,
        UnitType = m.UnitType,
        UnitDefinition = m.UnitDefinition,
    };

    private static UnitOfExecution ToUnitOfExecution(this UnitOfExecutionEntity e) => new()
    {
        ProjectName = e.ProjectName,
        ViewName = e.ViewName,
        AnnotationKey = e.AnnotationKey,
        Name = e.Name,
        AnnotationType = e.AnnotationType,
        IsDisabled = e.IsDisabled,
        ValidFrom = e.ValidFrom,
        ExpiresAt = e.ExpiresAt,
        TimeZone = e.TimeZone,
        Title = e.Title,
        Description = e.Description,
        DocumentationLink = e.DocumentationLink,
        Labels = e.Labels,
        Values = e.Values,
        ResponsibilityKey = e.ResponsibilityKey,
        UnitKey = e.UnitKey,
        SubjectKey = e.SubjectKey,
        ContextKey = e.ContextKey,
        UsageKey = e.UsageKey,
        ExecutionKey = e.ExecutionKey,
        ResponsibilityName = e.ResponsibilityName,
        UnitName = e.UnitName,
        SubjectName = e.SubjectName,
        ContextName = e.ContextName,
    };

    private static UnitOfExecutionEntity ToUnitOfExecutionEntity(this UnitOfExecution m) => new()
    {
        Id = string.Empty,
        PartitionKey = string.Empty,
        ProjectName = m.ProjectName,
        ViewName = m.ViewName,
        AnnotationKey = m.AnnotationKey,
        Name = m.Name,
        AnnotationType = m.AnnotationType,
        IsDisabled = m.IsDisabled,
        ValidFrom = m.ValidFrom,
        ExpiresAt = m.ExpiresAt,
        TimeZone = m.TimeZone,
        Title = m.Title,
        Description = m.Description,
        DocumentationLink = m.DocumentationLink,
        Labels = m.Labels,
        Values = m.Values,
        ResponsibilityKey = m.ResponsibilityKey,
        UnitKey = m.UnitKey,
        SubjectKey = m.SubjectKey,
        ContextKey = m.ContextKey,
        UsageKey = m.UsageKey,
        ExecutionKey = m.ExecutionKey,
        ResponsibilityName = m.ResponsibilityName,
        UnitName = m.UnitName,
        SubjectName = m.SubjectName,
        ContextName = m.ContextName,
    };
}
