namespace _42.Platform.Storyteller.Mcp.Tools;

// Annotation type codes as the API takes them (rst, unt, sbt, usg, cnt, exe, uxe), lower-cased.
public static class TypeCodes
{
    public const string List = "rst, unt, sbt, usg, cnt, exe or uxe";

    public static string Parse(string? code, string parameter = "annotationType")
    {
        var text = code?.Trim() ?? string.Empty;

        return AnnotationTypeCodes.ValidCodes.ContainsKey(text)
            ? text.ToLowerInvariant()
            : throw new ToolInputException($"'{parameter}' is not an annotation type code ('{code}'); the codes are {List}.");
    }

    public static string? ParseOptional(string? code, string parameter = "annotationType")
    {
        return string.IsNullOrWhiteSpace(code) ? null : Parse(code, parameter);
    }
}
