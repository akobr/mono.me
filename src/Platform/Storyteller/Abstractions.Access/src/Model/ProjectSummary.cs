namespace _42.Platform.Storyteller;

// A project as one of its members sees it: no members and no policies.
public record class ProjectSummary
{
    // organization.project
    public required string Key { get; init; }

    // The caller's role on the project.
    public required AccountRole Role { get; init; }

    public string? Description { get; init; }

    // The project's settings: the names, colors and icons of its types, its default time zone and date format.
    public ProjectSettings? Settings { get; init; }
}
