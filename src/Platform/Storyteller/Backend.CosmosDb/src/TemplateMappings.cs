using System;
using _42.Platform.Storyteller.Entities;
using _42.Platform.Storyteller.Entities.Configurations;

namespace _42.Platform.Storyteller;

internal static class TemplateMappings
{
    public static ConfigurationTemplate ToConfigurationTemplate(this GenerateTemplateEntity e) => new()
    {
        AnnotationType = e.Name,
        Version = e.Version,
        Content = e.Content,
        Author = e.Author,
    };

    public static ConfigurationTemplate ToConfigurationTemplate(this GenerateTemplateHistoryEntity e) => new()
    {
        AnnotationType = e.Name,
        Version = e.Version,
        Content = e.Content,
        Author = e.Author,
    };

    public static ConfigurationVersion ToConfigurationVersion(this GenerateTemplateEntity e) => new()
    {
        Version = (uint)e.Version,
        Author = e.Author,
        CreationTime = e.GetLastUpdatedTime(),
        ExpirationTime = DateTimeOffset.MaxValue,
    };

    public static ConfigurationVersion ToConfigurationVersion(this GenerateTemplateHistoryEntity e) => new()
    {
        Version = (uint)e.Version,
        Author = e.Author,
        CreationTime = e.CreationTime,
        ExpirationTime = e.GetExpirationTime(),
    };

    public static GenerateTemplateHistoryEntity ToHistory(this GenerateTemplateEntity e) => new()
    {
        PartitionKey = e.PartitionKey,
        Id = $"{EntityIdPrefixTypes.GenerateTemplateVersion}.{e.Name}.{e.Version}",
        AnnotationKey = e.AnnotationKey,
        Name = e.Name,
        ProjectName = e.ProjectName,
        ViewName = e.ViewName,
        Version = e.Version,
        Content = e.Content,
        Author = e.Author,
        CreationTime = e.GetLastUpdatedTime(),
    };
}
