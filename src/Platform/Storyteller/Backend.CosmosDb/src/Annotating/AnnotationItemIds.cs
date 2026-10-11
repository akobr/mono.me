using _42.Platform.Storyteller.Entities;

namespace _42.Platform.Storyteller.Annotating;

internal static class AnnotationItemIds
{
    public static string Annotation(string view, string key) => $"{view}.{key}";

    public static string Configuration(string view, string key) => $"{view}.{EntityIdPrefixTypes.Configuration}.{key}";

    public static string ConfigurationVersion(string view, string key, ulong version) => $"{view}.{EntityIdPrefixTypes.ConfigurationVersion}.{key}.{version}";

    public static string Audit(string view, string key) => $"{view}.{EntityIdPrefixTypes.AnnotationAudit}.{key}";
}
