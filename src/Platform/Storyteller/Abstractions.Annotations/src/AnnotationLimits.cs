namespace _42.Platform.Storyteller;

// Maximal lengths of the free-text members of an annotation, checked on every write.
public static class AnnotationLimits
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 10_000;
    public const int DocumentationLinkMaxLength = 2048;
}
