namespace _42.Platform.Storyteller.Access.Models;

public record class MachineAccessCreate
{
    public required string Organization { get; set; }

    public required string Project { get; set; }

    public required MachineAccessScope Scope { get; init; } = MachineAccessScope.DefaultRead;

    public string? AnnotationKey { get; init; }
}
