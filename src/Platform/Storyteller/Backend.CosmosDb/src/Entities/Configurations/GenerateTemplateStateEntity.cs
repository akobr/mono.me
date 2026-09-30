namespace _42.Platform.Storyteller.Entities.Configurations;

/// <summary>
/// Per-template bookkeeping, it outlives deletions of the template and expirations of its history.
/// </summary>
public record class GenerateTemplateStateEntity : Entity
{
    /// <summary>
    /// The highest version ever allocated to the template, versions are never reused.
    /// </summary>
    public ulong LastVersion { get; init; }

    /// <summary>
    /// A committed template change whose cache invalidation hasn't finished yet.
    /// </summary>
    public bool IsInvalidationPending { get; init; }
}
