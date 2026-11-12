namespace _42.Platform.Storyteller.DbCreator.DataSets;

// One annotation of annotations.jsonl (a generator line stands for many), or why it cannot be written: the error code
// and message of a file of an import. Key is null when the line has no key that parses.
public sealed record class DataSetAnnotationLine
{
    // 1-based
    public required int Line { get; init; }

    public AnnotationKey? Key { get; init; }

    public Annotation? Annotation { get; init; }

    public string? ErrorCode { get; init; }

    public string? Message { get; init; }
}
