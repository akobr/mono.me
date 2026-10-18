namespace _42.Platform.Storyteller.Configuring;

// Which annotation types descend from which (AnnotationHierarchy). A descendant-type schema exists only for such a
// pair: the schema writes reject any other, and an import fails it.
public static class DescendantTypeRules
{
    // The pairs (ancestor type, descendant type) of AnnotationHierarchy, read from one sample key of every type.
    private static readonly HashSet<(string Ancestor, string Descendant)> Pairs = new[]
        {
            AnnotationKey.CreateResponsibility("x"),
            AnnotationKey.CreateUnit("x", "x"),
            AnnotationKey.CreateSubject("x"),
            AnnotationKey.CreateUsage("x", "x"),
            AnnotationKey.CreateContext("x", "x"),
            AnnotationKey.CreateExecution("x", "x", "x"),
            AnnotationKey.CreateUnitOfExecution("x", "x", "x", "x"),
        }
        .SelectMany(AnnotationHierarchy.GetAncestorSchemaSources)
        .Select(source => (source.Ancestor.TypeCode, source.DescendantTypeCode))
        .ToHashSet();

    // Whether annotations of the descendant type can descend from the ancestor key; type codes in any case.
    public static bool CanDescend(AnnotationKey ancestorKey, string descendantType)
    {
        return Pairs.Contains((ancestorKey.TypeCode.ToLowerInvariant(), descendantType.ToLowerInvariant()));
    }

    // The same text for a rejected write and a failed import: the key as given, the type code in lower case.
    public static string GetCannotDescendMessage(AnnotationKey ancestorKey, string descendantType)
    {
        return $"The type {descendantType.ToLowerInvariant()} cannot descend from {ancestorKey}, so it has no descendant-type schema there.";
    }

    public static void EnsureCanDescend(AnnotationKey ancestorKey, string descendantType)
    {
        if (!CanDescend(ancestorKey, descendantType))
        {
            throw new InvalidInputException(GetCannotDescendMessage(ancestorKey, descendantType), ErrorCodes.InvalidKey);
        }
    }
}
