namespace _42.Platform.Storyteller.Access;

public interface IMachineAccess
{
    string Id { get; }

    string ObjectId { get; }

    string AccessKey { get; }

    MachineAccessScope Scope { get; }

    string? AnnotationKey { get; }
}
