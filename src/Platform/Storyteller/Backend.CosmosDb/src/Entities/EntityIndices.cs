namespace _42.Platform.Storyteller.Entities;

public record class EntityIndices
{
    public required string PartitionKey { get; init; }

    public required string Id { get; init; }
}
