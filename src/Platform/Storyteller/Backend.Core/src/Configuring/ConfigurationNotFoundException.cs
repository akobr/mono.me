namespace _42.Platform.Storyteller.Configuring;

public class ConfigurationNotFoundException(string annotationKey)
    : Exception($"Configuration for '{annotationKey}' does not exist or has no content to patch.")
{
    public string AnnotationKey { get; } = annotationKey;
}
