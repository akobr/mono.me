namespace _42.Platform.Storyteller.Mcp.Tools;

public static class AnnotationKeys
{
    public const string Format =
        "rst.{responsibility}, sbt.{subject}, unt.{responsibility}.{unit}, usg.{subject}.{responsibility}, cnt.{subject}.{context}, "
        + "exe.{subject}.{responsibility}.{context} or uxe.{subject}.{responsibility}.{context}.{unit}";

    public static AnnotationKey Parse(string? key, string parameter = "key")
    {
        return AnnotationKey.TryParse(key ?? string.Empty, out var parsed)
            ? parsed
            : throw new ToolInputException($"'{parameter}' is not an annotation key ('{key}'); the forms are {Format}.");
    }
}
