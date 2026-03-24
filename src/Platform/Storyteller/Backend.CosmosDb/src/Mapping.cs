using _42.Platform.Storyteller.Entities.Configurations;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller;

public static class Mapping
{
    public static Configuration ToConfiguration(this ConfigurationEntity @this, JObject? content = null)
    {
        return new Configuration
        {
            AnnotationKey = @this.AnnotationKey,
            Version = @this.Version,
            Author = @this.Author,
            Content = content is null ? @this.CalculatedContent : content,
            Hash = @this.CalculatedContentHash,
            Labels = @this.Labels,
            Values = @this.Values,
        };
    }

    public static Configuration ToConfigurationFromContent(this ConfigurationEntity @this)
    {
        return new Configuration
        {
            AnnotationKey = @this.AnnotationKey,
            Version = @this.Version,
            Author = @this.Author,
            Content = @this.Content,
            Hash = null,
            Labels = @this.Labels,
            Values = @this.Values,
        };
    }

    public static Configuration ToConfiguration(this ConfigurationHistoryEntity @this)
    {
        return new Configuration
        {
            AnnotationKey = @this.AnnotationKey,
            Version = @this.Version,
            Author = @this.Author,
            Content = @this.Content,
            Hash = null,
        };
    }

    public static Configuration ToConfiguration(this JObject @this, string annotationKey)
    {
        return new Configuration
        {
            AnnotationKey = annotationKey,
            Version = 0,
            Author = "system",
            Hash = null,
            Content = @this,
        };
    }
}
