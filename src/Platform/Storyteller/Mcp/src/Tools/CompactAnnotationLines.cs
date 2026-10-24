namespace _42.Platform.Storyteller.Mcp.Tools;

// The keys of create_annotations, computed from its lines as the API computes them (CreateAnnotationsFromStringAsync):
// a line that starts with a type code and parses is a key; otherwise its 1 to 4 names are a subject, a usage, an
// execution or a unit of execution. The tool sends the computed keys, so the keys it checks are the keys created.
// Lines the API would skip or read against their apparent intent are refused instead.
public static class CompactAnnotationLines
{
    public const string Format =
        "subject (sbt), subject.responsibility (usg), subject.responsibility.context (exe) or "
        + "subject.responsibility.context.unit (uxe)";

    public static AnnotationKey ToKey(string line)
    {
        var text = line.Trim();
        var segments = text.Split(Constants.KeySeparators, StringSplitOptions.RemoveEmptyEntries);
        var compact = FromNames(segments);

        if (segments.Length > 0
            && segments[0].Length == 3
            && AnnotationTypeCodes.ValidCodes.ContainsKey(segments[0]))
        {
            if (AnnotationKey.TryParse(text, out var key))
            {
                return key;
            }

            throw new ToolInputException(
                $"The line '{text}' starts with the type code '{segments[0]}' but is not a key ({AnnotationKeys.Format}). "
                + (compact is null
                    ? "The API would skip it."
                    : $"As a compact line the API would create {compact}; write that key in full if it is meant."));
        }

        return compact ?? throw new ToolInputException(
            $"The line '{text}' has {segments.Length} names; a compact line is {Format}, and a key starts with its type code.");
    }

    private static AnnotationKey? FromNames(string[] names)
    {
        return names.Length switch
        {
            1 => AnnotationKey.CreateSubject(names[0]),
            2 => AnnotationKey.CreateUsage(names[0], names[1]),
            3 => AnnotationKey.CreateExecution(names[0], names[1], names[2]),
            4 => AnnotationKey.CreateUnitOfExecution(names[0], names[1], names[2], names[3]),
            _ => null,
        };
    }
}
