using _42.Platform.Storyteller;

namespace _42.Platform.Cli.Model;

public static class AnnotationKeyCasing
{
    // The API stores an annotation under its key as written, so a type code in another case would be a second annotation
    // next to the real one. StorytellerSetCommand has a private copy until 424-210 lands.
    public static AnnotationKey WithLowerCaseTypeCode(this AnnotationKey key)
    {
        var text = key.ToString();
        return AnnotationKey.Parse(key.TypeCode.ToLowerInvariant() + text[key.TypeCode.Length..]);
    }
}
