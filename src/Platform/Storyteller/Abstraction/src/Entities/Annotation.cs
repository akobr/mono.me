using System;

namespace _42.Platform.Storyteller.Entities;

public record class Annotation : ExtendableStoredItem
{
    public required AnnotationType AnnotationType { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? DocumentationLink { get; init; }

    public bool? IsDisabled { get; init; }

    public DateTimeOffset? ValidFrom { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public string? TimeZone { get; init; }
}
