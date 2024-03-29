namespace _42.Platform.Storyteller.Entities;

public record class StoredItem
{
    public required string PartitionKey { get; init; }

    public string Id => $"{ViewName}.{AnnotationKey}";

    public required string ProjectName { get; init; }

    public required string ViewName { get; init; }

    public required string AnnotationKey { get; init; }

    public required string Name { get; init; }
}
