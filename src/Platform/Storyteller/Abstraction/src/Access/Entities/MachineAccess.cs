
namespace _42.Platform.Storyteller.Access.Entities;

public record class MachineAccess
{
    public required string PartitionKey { get; init; }

    public required string Id { get; init; }

    public required string ObjectId { get; init; }

    public required string AccessKey { get; init; }

    public required MachineAccessScope Scope { get; init; }

    public string? AnnotationKey { get; init; }
}
