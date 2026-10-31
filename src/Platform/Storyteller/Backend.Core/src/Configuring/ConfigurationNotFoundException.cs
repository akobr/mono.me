namespace _42.Platform.Storyteller.Configuring;

public class ConfigurationNotFoundException(string annotationKey)
    : Exception($"Configuration for '{annotationKey}' has no stored version to patch: it was never written, or it was deleted.")
{
    public string AnnotationKey { get; } = annotationKey;
}
