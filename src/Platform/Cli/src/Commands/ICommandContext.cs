namespace _42.Platform.Cli.Commands;

public interface ICommandContext
{
    string OrganizationName { get; }

    string ProjectName { get; }

    string ViewName { get; }

    void TrySetExplicitTarget(string? projectKey, string? viewName);
}
