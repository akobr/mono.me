using System;

namespace _42.Platform.Storyteller;

// A view of a project: an isolated catalog of annotations, configurations, templates and schemas.
public record class View
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public string? CreatedBy { get; init; }

    // The view "default", which always exists.
    public bool IsDefault { get; init; }

    // False for a view found only in the data (discovery) or the implicit default view.
    public bool IsRegistered { get; init; }
}

public record class ViewCreate
{
    public required string Name { get; init; }

    public string? Description { get; init; }
}

public record class ViewUpdate
{
    public string? Description { get; init; }
}
