namespace _42.Platform.Storyteller.Configuring;

public class ConfigurationNotFoundException(string annotationKey)
    : Exception($"Configuration for '{annotationKey}' has no stored version to patch: it was never written, or it was deleted. A PUT or POST writes a first document.")
{
    public string AnnotationKey { get; } = annotationKey;
}
