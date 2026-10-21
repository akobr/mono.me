namespace _42.Platform.Storyteller;

// A project as one of its members sees it: no members and no policies.
public record class ProjectSummary
{
    // organization.project
    public required string Key { get; init; }

    // The caller's role on the project.
    public required AccountRole Role { get; init; }

    public string? Description { get; init; }
}
