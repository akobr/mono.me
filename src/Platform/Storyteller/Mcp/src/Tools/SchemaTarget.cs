namespace _42.Platform.Storyteller.Mcp.Tools;

// The schema document a schema tool addresses. A type schema applies to every configuration of a type, an annotation
// schema to one key, and a descendant-type schema sits on an ancestor key and applies to its descendants of a type.
public sealed record SchemaTarget(SchemaTarget.Level Kind, string? AnnotationType, string? Key)
{
    public const string Levels =
        "level \"type\" (needs annotationType), \"annotation\" (needs key) or \"descendantType\" (needs key, the ancestor, "
        + "and annotationType, the type of its descendants)";

    public enum Level
    {
        Type,
        Annotation,
        DescendantType,
    }

    public static SchemaTarget Parse(string? level, string? annotationType, string? key)
    {
        var kind = level?.Trim().ToLowerInvariant() switch
        {
            "type" => Level.Type,
            "annotation" => Level.Annotation,
            "descendanttype" or "descendant-type" or "descendant_type" => Level.DescendantType,
            _ => throw new ToolInputException($"'level' is not a schema level ('{level}'); it is {Levels}."),
        };

        var needsType = kind is Level.Type or Level.DescendantType;
        var needsKey = kind is Level.Annotation or Level.DescendantType;

        if (needsType == string.IsNullOrWhiteSpace(annotationType))
        {
            throw new ToolInputException(needsType
                ? $"The {kind} level needs 'annotationType'; {Levels}."
                : $"The {kind} level takes no 'annotationType'; {Levels}.");
        }

        if (needsKey == string.IsNullOrWhiteSpace(key))
        {
            throw new ToolInputException(needsKey
                ? $"The {kind} level needs 'key'; {Levels}."
                : $"The {kind} level takes no 'key'; {Levels}.");
        }

        return new SchemaTarget(
            kind,
            needsType ? TypeCodes.Parse(annotationType) : null,
            needsKey ? AnnotationKeys.Parse(key).ToString() : null);
    }
}
