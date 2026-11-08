namespace _42.Platform.Storyteller.Sdk.Hosting.Reading;

// The snapshot type of each annotation type, as the mapper builds them, and back.
internal static class SnapshotTypes
{
    private static readonly IReadOnlyDictionary<AnnotationType, Type> Types = new Dictionary<AnnotationType, Type>
    {
        [AnnotationType.Responsibility] = typeof(StorytellerResponsibility),
        [AnnotationType.Unit] = typeof(StorytellerUnit),
        [AnnotationType.Subject] = typeof(StorytellerSubject),
        [AnnotationType.Usage] = typeof(StorytellerUsage),
        [AnnotationType.Context] = typeof(StorytellerContext),
        [AnnotationType.Execution] = typeof(StorytellerExecution),
        [AnnotationType.UnitOfExecution] = typeof(StorytellerUnitOfExecution),
    };

    private static readonly IReadOnlyDictionary<Type, AnnotationType> AnnotationTypes = Types.ToDictionary(pair => pair.Value, pair => pair.Key);

    public static Type Of(AnnotationType type)
    {
        return Types.TryGetValue(type, out var snapshotType)
            ? snapshotType
            : throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown annotation type.");
    }

    // null for StorytellerAnnotation itself, which names no type.
    public static AnnotationType? TypeOf(Type snapshotType)
    {
        ArgumentNullException.ThrowIfNull(snapshotType);

        return AnnotationTypes.TryGetValue(snapshotType, out var type) ? type : null;
    }

    // The type a snapshot type names, for a generic member whose type argument must name one.
    public static AnnotationType Require<TAnnotation>(string parameterName)
        where TAnnotation : StorytellerAnnotation
    {
        return TypeOf(typeof(TAnnotation)) ?? throw new ArgumentException(
            $"{typeof(TAnnotation).Name} names no annotation type; use {string.Join(", ", Types.Values.Select(type => type.Name))}.",
            parameterName);
    }
}
