namespace _42.Platform.Cli.Commands;

public interface ICommandContext
{
    string OrganizationName { get; }

    string ProjectName { get; }

    string ViewName { get; }

    // False only for a machine sign-in without a project from -p, STORYTELLER_PROJECT or its credential.
    bool IsProjectKnown { get; }

    void TrySetExplicitTarget(string? projectKey, string? viewName);
}
