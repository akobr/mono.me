using _42.Platform.Cli.Configuration;
using Microsoft.Extensions.Options;

namespace _42.Platform.Cli.Commands;

public class CommandContext : ICommandContext
{
    private readonly AccessDefaultOptions _accessOptions;

    public CommandContext(IOptions<AccessDefaultOptions> accessDefaultOptions)
    {
        _accessOptions = accessDefaultOptions.Value;

        OrganizationName = _accessOptions.OrganizationName ?? Platform.Storyteller.Constants.DefaultTenantName;
        ProjectName = _accessOptions.ProjectName ?? Platform.Storyteller.Constants.DefaultProjectName;
        ViewName = _accessOptions.ViewName ?? Platform.Storyteller.Constants.DefaultViewName;
    }

    public string OrganizationName { get; private set; }

    public string ProjectName { get; private set; }

    public string ViewName { get; private set; }

    public void TrySetExplicitTarget(string? projectKey, string? viewName)
    {
        if (!string.IsNullOrWhiteSpace(projectKey)
            && projectKey.Contains('.'))
        {
            var separatorIndex = projectKey.IndexOf('.');
            OrganizationName = projectKey[..separatorIndex];
            ProjectName = projectKey[(separatorIndex + 1)..];
        }

        if (!string.IsNullOrWhiteSpace(viewName))
        {
            ViewName = viewName;
        }
    }
}
