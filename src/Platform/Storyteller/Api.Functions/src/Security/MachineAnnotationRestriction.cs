namespace _42.Platform.Storyteller.Api.Security;

// The annotation a machine is restricted to (MachineAccess.AnnotationKey). A restricted machine reaches that annotation
// and its descendants, in every view of its project, and nothing that spans the whole project. A machine without a key
// reaches the whole project; a stored key that does not parse reaches nothing. Ancestors still reach the machine through
// the effective configuration of its annotation, which is what inheritance is for.
public sealed class MachineAnnotationRestriction
{
    private readonly AnnotationKey? _root;
    private readonly string? _rootText;

    private MachineAnnotationRestriction(string? key, AnnotationKey? root, string? rootText)
    {
        Key = key;
        _root = root;
        _rootText = rootText;
    }

    public static MachineAnnotationRestriction None { get; } = new(null, null, null);

    // The key as it is stored on the machine; null for a machine of the whole project.
    public string? Key { get; }

    public bool IsRestricted => Key is not null;

    public static MachineAnnotationRestriction From(string? annotationKey)
    {
        if (string.IsNullOrWhiteSpace(annotationKey))
        {
            return None;
        }

        return TryNormalize(annotationKey, out var normalized) && normalized is not null
            ? new MachineAnnotationRestriction(annotationKey, AnnotationKey.Parse(normalized), normalized)
            : new MachineAnnotationRestriction(annotationKey, null, null);
    }

    // The canonical form of a key: the type code in lower case, the names as they are, empty segments dropped. Null or
    // whitespace is valid and normalizes to null (no restriction); false when the key does not parse.
    public static bool TryNormalize(string? annotationKey, out string? normalized)
    {
        normalized = null;

        if (string.IsNullOrWhiteSpace(annotationKey))
        {
            return true;
        }

        if (!AnnotationKey.TryParse(annotationKey, out var key))
        {
            return false;
        }

        normalized = ToCanonical(key);
        return true;
    }

    // The annotation itself or one of its descendants. Names are compared exactly, the type code in any case.
    public bool Allows(AnnotationKey key)
    {
        if (!IsRestricted)
        {
            return true;
        }

        return _root is not null
            && (string.Equals(ToCanonical(key), _rootText, StringComparison.Ordinal) || key.IsDescendantOf(_root));
    }

    // A key that does not parse is outside of every restriction.
    public bool Allows(string? annotationKey)
    {
        if (!IsRestricted)
        {
            return true;
        }

        return annotationKey is not null
            && AnnotationKey.TryParse(annotationKey, out var key)
            && Allows(key);
    }

    public void EnsureAllows(IEnumerable<string?> annotationKeys)
    {
        if (!IsRestricted)
        {
            return;
        }

        foreach (var annotationKey in annotationKeys)
        {
            if (!Allows(annotationKey))
            {
                throw Denied($"'{annotationKey}' is outside of it");
            }
        }
    }

    // For operations that span the whole project or view: lists, key/values, templates, type-level schemas, transfers.
    public void EnsureUnrestricted()
    {
        if (IsRestricted)
        {
            throw Denied("the operation spans the whole project");
        }
    }

    private static string ToCanonical(AnnotationKey key)
    {
        var text = key.ToString();
        var separator = text.IndexOf(Constants.DefaultKeySeparatorChar);
        return string.Concat(text[..separator].ToLowerInvariant(), text[separator..]);
    }

    private AccessDeniedException Denied(string reason)
    {
        var message = _root is null
            ? $"The machine is restricted to '{Key}', which is not a valid annotation key."
            : $"The machine is restricted to the annotation '{_rootText}' and its descendants; {reason}.";
        return new AccessDeniedException(message, ErrorCodes.AnnotationRestricted);
    }
}
