using System;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Configuration;
using _42.Platform.Storyteller;
using _42.Platform.Storyteller.Sdk.Credentials;
using Microsoft.Extensions.Options;

namespace _42.Platform.Cli.Commands;

// The organization, project and view a command works on. The first that applies wins:
// -p/-v, then STORYTELLER_PROJECT/STORYTELLER_VIEW, then for a machine STORYTELLER_KEY and then the project of its
// credential, for a user access.default.json. The view of STORYTELLER_KEY belongs to the project of the key, as the view
// of access.default.json belongs to the project of the file. A machine never falls back to access.default.json or
// house.main, as the file survives between jobs on a self-hosted runner.
public class CommandContext : ICommandContext
{
    private readonly IMachineSignIn _machine;

    // The project whose view came from access.default.json; that view belongs to it only.
    private readonly string? _fileViewProjectKey;

    // STORYTELLER_KEY of a machine; a user never reads it.
    private readonly FullKey? _key;

    public CommandContext(IOptions<AccessDefaultOptions> accessDefaultOptions, IMachineSignIn machine)
    {
        _machine = machine;

        var access = accessDefaultOptions.Value;
        var settings = machine.Settings;
        var identity = machine.TryGetIdentity();
        var key = machine.IsConfigured ? settings.Key : null;
        var projectFromFile = false;

        if (MachineSignInSettings.TrySplitProject(settings.Project, out var organization, out var project))
        {
            OrganizationName = organization;
            ProjectName = project;
            IsProjectKnown = true;
        }
        else if (key is not null)
        {
            OrganizationName = key.OrganizationName;
            ProjectName = key.ProjectName;
            IsProjectKnown = true;
        }
        else if (machine.IsConfigured)
        {
            IsProjectKnown = identity is { Organization: not null, Project: not null };
            OrganizationName = identity?.Organization ?? Platform.Storyteller.Constants.DefaultTenantName;
            ProjectName = identity?.Project ?? Platform.Storyteller.Constants.DefaultProjectName;
        }
        else
        {
            OrganizationName = access.OrganizationName ?? Platform.Storyteller.Constants.DefaultTenantName;
            ProjectName = access.ProjectName ?? Platform.Storyteller.Constants.DefaultProjectName;
            IsProjectKnown = true;
            projectFromFile = true;
        }

        // A view of the file belongs to the project of the file, a view of the key to the project of the key.
        _fileViewProjectKey = settings.View is null && projectFromFile && access.ViewName is not null
            ? $"{OrganizationName}.{ProjectName}"
            : null;
        _key = key;
        ViewName = settings.View
            ?? (projectFromFile ? access.ViewName : null)
            ?? GetKeyView()
            ?? Platform.Storyteller.Constants.DefaultViewName;
    }

    public string OrganizationName { get; private set; }

    public string ProjectName { get; private set; }

    public string ViewName { get; private set; }

    public bool IsProjectKnown { get; private set; }

    // Throws AuthenticationException (Configuration) when the target cannot be right: a malformed
    // STORYTELLER_PROJECT or STORYTELLER_KEY, or a machine without a project or with another project than its
    // credential. A machine is then bound to the target.
    public void TrySetExplicitTarget(string? projectKey, string? viewName)
    {
        var isExplicitProject = !string.IsNullOrWhiteSpace(projectKey) && projectKey.Contains('.');
        var settings = _machine.Settings;

        if (isExplicitProject)
        {
            var separatorIndex = projectKey!.IndexOf('.');
            OrganizationName = projectKey[..separatorIndex];
            ProjectName = projectKey[(separatorIndex + 1)..];
            IsProjectKnown = true;

            if (_fileViewProjectKey is not null && !string.Equals(_fileViewProjectKey, projectKey, StringComparison.OrdinalIgnoreCase))
            {
                ViewName = Platform.Storyteller.Constants.DefaultViewName;
            }

            // The view of the key follows the project of the key; STORYTELLER_VIEW stays.
            if (_key is not null && settings.View is null)
            {
                ViewName = GetKeyView() ?? Platform.Storyteller.Constants.DefaultViewName;
            }
        }

        if (!string.IsNullOrWhiteSpace(viewName))
        {
            ViewName = viewName;
        }

        if (!isExplicitProject && settings.Project is not null && !MachineSignInSettings.TrySplitProject(settings.Project, out _, out _))
        {
            throw Invalid($"{TargetVariables.PROJECT} must be <organization>.<project>.");
        }

        // Only where the key would pick the target: -p and STORYTELLER_PROJECT win over it.
        if (!isExplicitProject && settings.Project is null && _machine.IsConfigured && settings.IsKeyInvalid)
        {
            throw settings.InvalidKey();
        }

        if (!_machine.IsConfigured)
        {
            return;
        }

        // The real reason first, e.g. a certificate that does not load.
        var identity = _machine.GetIdentity();

        if (!IsProjectKnown)
        {
            throw Invalid(
                $"No project is set for the machine: set {TargetVariables.PROJECT}=<organization>.<project>, set {StorytellerVariables.KEY} to a full key of the project, or pass -p.");
        }

        if (identity is { Organization: not null, Project: not null }
            && (!string.Equals(identity.Organization, OrganizationName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(identity.Project, ProjectName, StringComparison.OrdinalIgnoreCase)))
        {
            throw Invalid(
                $"The machine credentials in {string.Join(", ", identity.Variables)} belong to {identity.Organization}.{identity.Project}, not {OrganizationName}.{ProjectName}.");
        }

        // Only after every check: a federated machine exchanges at the token endpoint of this project.
        _machine.BindTarget(OrganizationName, ProjectName);
    }

    private static AuthenticationException Invalid(string message)
    {
        return new AuthenticationException(AuthenticationFailureReason.Configuration, message);
    }

    // The view of STORYTELLER_KEY while the target is the project of the key.
    private string? GetKeyView()
    {
        return _key is not null
            && string.Equals(_key.OrganizationName, OrganizationName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_key.ProjectName, ProjectName, StringComparison.OrdinalIgnoreCase)
                ? _key.ViewName
                : null;
    }
}
