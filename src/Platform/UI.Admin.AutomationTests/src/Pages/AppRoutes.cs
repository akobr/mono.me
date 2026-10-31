namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The admin UI's routes, relative to Target:BaseUrl, for an organization, a project and a view: the target's from
// AppRoutes.For(Target), another project's with "routes with { Project = … }". App.GotoAsync opens them and
// App.ExpectAtAsync checks the current one.
public sealed record AppRoutes(string Organization, string Project, string View)
{
    // The view every new project starts with.
    public const string DefaultView = "default";

    // The same for every target; an instance member like the other routes, so that Routes.Organizations works too.
    public string Organizations => "orgs";

    public string OrganizationOverview => Organization;

    public string Settings => $"{Organization}/{Project}/settings";

    public string Views => $"{Organization}/{Project}/views";

    public string Annotations => $"{ViewPath}/annotations";

    public string Configurations => $"{ViewPath}/configurations";

    public string KeyValues => $"{ViewPath}/key-values";

    private string ViewPath => $"{Organization}/{Project}/{View}";

    public static AppRoutes For(PlatformTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new AppRoutes(target.Organization, target.Project, target.View);
    }

    // The detail of an annotation, over the list.
    public string Annotation(string key)
    {
        return $"{Annotations}/{Uri.EscapeDataString(key)}";
    }

    // The configuration of an annotation (its Edit tab).
    public string Configuration(string key)
    {
        return $"{Configurations}/{Uri.EscapeDataString(key)}";
    }

    // The detail of a key value; the store keeps its keys in lower case.
    public string KeyValue(string key)
    {
        return $"{KeyValues}/{Uri.EscapeDataString(key.ToLowerInvariant())}";
    }
}
