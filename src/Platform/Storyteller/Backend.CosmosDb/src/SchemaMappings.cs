using System;
using _42.Platform.Storyteller.Entities;
using _42.Platform.Storyteller.Entities.Configurations;

namespace _42.Platform.Storyteller;

internal static class SchemaMappings
{
    public static ConfigurationSchema ToConfigurationSchema(this ConfigurationSchemaEntity entity, string? annotationType, string? annotationKey) => new()
    {
        View = entity.ViewName,
        AnnotationType = annotationType,
        AnnotationKey = annotationKey,
        Version = entity.Version,
        Content = entity.Content,
        Author = entity.Author,
    };

    public static ConfigurationSchema ToConfigurationSchema(this ConfigurationSchemaHistoryEntity entity, string? annotationType, string? annotationKey) => new()
    {
        View = entity.ViewName,
        AnnotationType = annotationType,
        AnnotationKey = annotationKey,
        Version = entity.Version,
        Content = entity.Content,
        Author = entity.Author,
    };

    public static ConfigurationVersion ToConfigurationVersion(this ConfigurationSchemaEntity entity) => new()
    {
        Version = (uint)entity.Version,
        Author = entity.Author,
        CreationTime = entity.GetLastUpdatedTime(),
        ExpirationTime = DateTimeOffset.MaxValue,
    };

    public static ConfigurationVersion ToConfigurationVersion(this ConfigurationSchemaHistoryEntity entity) => new()
    {
        Version = (uint)entity.Version,
        Author = entity.Author,
        CreationTime = entity.CreationTime,
        ExpirationTime = entity.GetExpirationTime(),
    };

    public static ConfigurationSchemaHistoryEntity ToHistory(this ConfigurationSchemaEntity entity, string historyId) => new()
    {
        PartitionKey = entity.PartitionKey,
        Id = historyId,
        AnnotationKey = entity.AnnotationKey,
        Name = entity.Name,
        ProjectName = entity.ProjectName,
        ViewName = entity.ViewName,
        Version = entity.Version,
        Content = entity.Content,
        Author = entity.Author,
        CreationTime = entity.GetLastUpdatedTime(),
    };
}
