namespace _42.Platform.Storyteller;

public interface IMachineAccess
{
    string Id { get; }

    string ObjectId { get; }

    string AccessKey { get; }

    MachineAccessScope Scope { get; }

    string? AnnotationKey { get; }
}
