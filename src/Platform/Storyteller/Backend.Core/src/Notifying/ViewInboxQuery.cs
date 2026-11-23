namespace _42.Platform.Storyteller.Notifying;

public sealed record ViewInboxQuery
{
    public const int PageSize = 50;

    public ViewInboxFilter Filter { get; init; }

    // The caller's role on the project.
    public required AccountRole Role { get; init; }

    public string? ContinuationToken { get; init; }
}
