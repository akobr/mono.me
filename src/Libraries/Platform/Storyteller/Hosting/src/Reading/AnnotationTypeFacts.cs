namespace _42.Platform.Storyteller.Sdk.Hosting.Reading;

// What the catalog knows of each annotation type: the words of its messages, the parts of its key, the descendant types
// the API lists under it (AnnotationsHttp.GetDescendants), and the path segment of their list.
internal static class AnnotationTypeFacts
{
    private static readonly IReadOnlyDictionary<AnnotationType, AnnotationType[]> Descendants = new Dictionary<AnnotationType, AnnotationType[]>
    {
        [AnnotationType.Responsibility] = [AnnotationType.Unit, AnnotationType.Usage, AnnotationType.Execution, AnnotationType.UnitOfExecution],
        [AnnotationType.Unit] = [AnnotationType.UnitOfExecution],
        [AnnotationType.Subject] = [AnnotationType.Usage, AnnotationType.Context, AnnotationType.Execution, AnnotationType.UnitOfExecution],
        [AnnotationType.Usage] = [AnnotationType.Execution, AnnotationType.UnitOfExecution],
        [AnnotationType.Context] = [AnnotationType.Execution, AnnotationType.UnitOfExecution],
        [AnnotationType.Execution] = [AnnotationType.UnitOfExecution],
        [AnnotationType.UnitOfExecution] = [],
    };

    // The types whose annotations the API lists under an annotation of the type, …/annotations/{key}/{segment}.
    public static IReadOnlyList<AnnotationType> DescendantsOf(AnnotationType type)
    {
        return Descendants.TryGetValue(type, out var descendants)
            ? descendants
            : throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown annotation type.");
    }

    // The path segment of the list of a descendant type.
    public static string Segment(AnnotationType type)
    {
        return type switch
        {
            AnnotationType.Usage => "usages",
            AnnotationType.Context => "contexts",
            AnnotationType.Unit => "units",
            AnnotationType.Execution => "executions",
            AnnotationType.UnitOfExecution => "units-of-execution",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No list of the type."),
        };
    }

    // "a responsibility", "an execution", …
    public static string Singular(AnnotationType type)
    {
        return type switch
        {
            AnnotationType.Responsibility => "a responsibility",
            AnnotationType.Unit => "a unit",
            AnnotationType.Subject => "a subject",
            AnnotationType.Usage => "a usage",
            AnnotationType.Context => "a context",
            AnnotationType.Execution => "an execution",
            AnnotationType.UnitOfExecution => "a unit of execution",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown annotation type."),
        };
    }

    // "responsibilities", "units of execution", …
    public static string Plural(AnnotationType type)
    {
        return type switch
        {
            AnnotationType.Responsibility => "responsibilities",
            AnnotationType.Unit => "units",
            AnnotationType.Subject => "subjects",
            AnnotationType.Usage => "usages",
            AnnotationType.Context => "contexts",
            AnnotationType.Execution => "executions",
            AnnotationType.UnitOfExecution => "units of execution",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown annotation type."),
        };
    }

    // The shape of its keys, "exe.{subject}.{responsibility}.{context}".
    public static string Pattern(AnnotationType type)
    {
        return type switch
        {
            AnnotationType.Responsibility => "rst.{responsibility}",
            AnnotationType.Unit => "unt.{responsibility}.{unit}",
            AnnotationType.Subject => "sbt.{subject}",
            AnnotationType.Usage => "usg.{subject}.{responsibility}",
            AnnotationType.Context => "cnt.{subject}.{context}",
            AnnotationType.Execution => "exe.{subject}.{responsibility}.{context}",
            AnnotationType.UnitOfExecution => "uxe.{subject}.{responsibility}.{context}.{unit}",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown annotation type."),
        };
    }

    // The names its keys carry.
    public static KeyParts PartsOf(AnnotationType type)
    {
        return type switch
        {
            AnnotationType.Responsibility => KeyParts.Responsibility,
            AnnotationType.Unit => KeyParts.Responsibility | KeyParts.Unit,
            AnnotationType.Subject => KeyParts.Subject,
            AnnotationType.Usage => KeyParts.Subject | KeyParts.Responsibility,
            AnnotationType.Context => KeyParts.Subject | KeyParts.Context,
            AnnotationType.Execution => KeyParts.Subject | KeyParts.Responsibility | KeyParts.Context,
            AnnotationType.UnitOfExecution => KeyParts.Subject | KeyParts.Responsibility | KeyParts.Context | KeyParts.Unit,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown annotation type."),
        };
    }

    // The name of a part in a key that has it.
    public static string NameOf(AnnotationKey key, KeyParts part)
    {
        return part switch
        {
            KeyParts.Subject => key.SubjectName,
            KeyParts.Responsibility => key.ResponsibilityName,
            KeyParts.Context => key.ContextName,
            KeyParts.Unit => key.UnitName,
            _ => throw new ArgumentOutOfRangeException(nameof(part), part, "One part of a key."),
        };
    }

    // "subject", "context", …
    public static string Word(KeyParts part)
    {
        return part switch
        {
            KeyParts.Subject => "subject",
            KeyParts.Responsibility => "responsibility",
            KeyParts.Context => "context",
            KeyParts.Unit => "unit",
            _ => throw new ArgumentOutOfRangeException(nameof(part), part, "One part of a key."),
        };
    }
}
