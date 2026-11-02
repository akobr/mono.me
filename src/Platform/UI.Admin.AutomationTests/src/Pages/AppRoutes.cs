using System.Text.RegularExpressions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The admin UI's routes (the file-based routes of src/Platform/ui.admin/src/app/pages), relative to Target:BaseUrl, for
// an organization, a project and a view: the target's from AppRoutes.For(Target), another project's with "routes with
// { Project = … }". App.GotoAsync opens them and App.ExpectAtAsync checks the current one. Keys are escaped as one path
// segment; WithQuery adds a query, and HrefEndingWith matches a link to a route. Sections add their own routes in
// AppRoutes.<Section>.cs.
public sealed partial record AppRoutes(string Organization, string Project, string View)
{
    // The view every new project starts with.
    public const string DefaultView = "default";

    // The tabs of Modeling (…/<view>/modeling/<tab>), in their order.
    private static readonly string[] ModelingTabs = ["schemas", "templates", "export-import", "deleted", "compare"];

    // The tabs of a configuration's detail besides Edit (Configuration(key)).
    private static readonly string[] ConfigurationTabs = ["effective", "resolved", "schema", "versions"];

    // The old tabs of an annotation's detail (…/annotations/<key>/<tab>) and of a graph node, which redirect to the
    // configuration's detail.
    private static readonly string[] OldDetailTabs = ["configuration", "schema", "versions"];

    // The old pages of a view (…/<view>/<page>[/…]), which redirect to Modeling's tabs.
    private static readonly string[] OldViewPages = ["schemas", "templates", "export-import", "deleted", "compare"];

    // The old pages of a project, which redirect: secrets to settings#secrets, certificates to
    // machines#shared-certificates, invitations to members#invitations, usage to monitor, audit to monitor?tab=audit.
    private static readonly string[] OldProjectPages = ["secrets", "certificates", "invitations", "usage", "audit"];

    // "": the start page, which opens the last context (the last project's view), else the organization or /orgs.
    // The global routes are instance members like the other routes, so that Routes.Account works too.
    public string Home => string.Empty;

    // Public: the profile, the memberships, the theme and the browser's preferences.
    public string Account => "account";

    // A new user without an account creates the first organization and project here.
    public string Onboarding => "onboarding";

    // The signed-in user's own invitations (Accept, Decline).
    public string MyInvitations => "invitations";

    // Public: the page that says the application is configured, or what is missing.
    public string Unsupported => "unsupported";

    public string Organizations => "orgs";

    public string OrganizationOverview => Organization;

    public string OrganizationMembers => $"{Organization}/members";

    // "<org>/<project>", which redirects to the default view's annotations (Project is the project's name).
    public string ProjectRoot => ProjectPath;

    public string Views => $"{ProjectPath}/views";

    // The project's members and invitations (Administrator and up); ?highlight=<account id> marks a member.
    public string Members => $"{ProjectPath}/members";

    // The project's machines and shared certificates.
    public string Machines => $"{ProjectPath}/machines";

    // "<org>/<project>/<view>" alone is not a page: the app shows "Page not found".
    public string ViewRoot => ViewPath;

    public string Annotations => $"{ViewPath}/annotations";

    public string Configurations => $"{ViewPath}/configurations";

    public string KeyValues => $"{ViewPath}/key-values";

    public string Graph => $"{ViewPath}/graph";

    // Redirects to its first tab, Schemas.
    public string Modeling => $"{ViewPath}/modeling";

    private string ProjectPath => $"{Organization}/{Project}";

    private string ViewPath => $"{Organization}/{Project}/{View}";

    public static AppRoutes For(PlatformTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new AppRoutes(target.Organization, target.Project, target.View);
    }

    // "<org>/<project>[/<view>]" (Target:RoleProjects), the view "default" when it has none.
    public static AppRoutes Parse(string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        var parts = project.Trim('/').Split('/');
        return parts.Length switch
        {
            2 => new AppRoutes(parts[0], parts[1], DefaultView),
            3 => new AppRoutes(parts[0], parts[1], parts[2]),
            _ => throw new ArgumentException($"'{project}' is not <organization>/<project>[/<view>].", nameof(project)),
        };
    }

    // Adds the parameters with a value to the query of a path, before its #fragment; a null value adds nothing, so
    // WithQuery(Routes.Annotations, ("q", filter), ("mode", null)) leaves out the mode.
    public static string WithQuery(string path, params (string Name, string? Value)[] parameters)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(parameters);

        var query = string.Join('&', parameters
            .Where(parameter => parameter.Value is not null)
            .Select(parameter => $"{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(parameter.Value!)}"));
        if (query.Length == 0)
        {
            return path;
        }

        var hash = path.IndexOf('#', StringComparison.Ordinal);
        var (beforeHash, fragment) = hash < 0 ? (path, string.Empty) : (path[..hash], path[hash..]);
        var separator = beforeHash.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{beforeHash}{separator}{query}{fragment}";
    }

    // /login: the sign-in page; returnTo is where a signed-in user continues (a same-origin path), error and
    // errorDescription the sign-in error it shows.
    public string Login(string? returnTo = null, string? error = null, string? errorDescription = null)
    {
        return WithQuery("login", ("returnTo", returnTo), ("error", error), ("error_description", errorDescription));
    }

    // /callback: where AuthKit returns; with an error it goes on to /login with the error.
    public string Callback(string? error = null, string? errorDescription = null)
    {
        return WithQuery("callback", ("error", error), ("error_description", errorDescription));
    }

    // The landing page of an invitation e-mail (public): ?invitation_token=…&id=….
    public string InvitationAccept(string? id = null, string? token = null)
    {
        return WithQuery("invitations/accept", ("invitation_token", token), ("id", id));
    }

    // The detail of a machine.
    public string Machine(string id)
    {
        return $"{Machines}/{Segment(id)}";
    }

    // The project's settings; section is the id of a section to scroll to, e.g. "secrets" (settings#secrets).
    public string Settings(string? section = null)
    {
        return section is null ? $"{ProjectPath}/settings" : $"{ProjectPath}/settings#{section}";
    }

    // Monitor (Administrator and up): the Usage tab, or ?tab=audit for the audit trail.
    public string Monitor(string? tab = null)
    {
        return tab is null or "usage" ? $"{ProjectPath}/monitor" : WithQuery($"{ProjectPath}/monitor", ("tab", tab));
    }

    // The detail of an annotation, over the list.
    public string Annotation(string key)
    {
        return $"{Annotations}/{Segment(key)}";
    }

    // The History tab of an annotation's detail.
    public string AnnotationHistory(string key)
    {
        return $"{Annotation(key)}/history";
    }

    // The configuration of an annotation (its Edit tab).
    public string Configuration(string key)
    {
        return $"{Configurations}/{Segment(key)}";
    }

    // Another tab of a configuration's detail: effective, resolved, schema or versions.
    public string ConfigurationTab(string key, string tab)
    {
        return $"{Configuration(key)}/{OneOf(tab, ConfigurationTabs, nameof(tab))}";
    }

    // The detail of a key value; the store keeps its keys in lower case.
    public string KeyValue(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return $"{KeyValues}/{Segment(key.ToLowerInvariant())}";
    }

    public string KeyValueVersions(string key)
    {
        return $"{KeyValue(key)}/versions";
    }

    // The graph with the detail of a node.
    public string GraphNode(string key)
    {
        return $"{Graph}/{Segment(key)}";
    }

    // The History tab of a graph node's detail.
    public string GraphNodeHistory(string key)
    {
        return $"{GraphNode(key)}/history";
    }

    // A tab of Modeling: schemas, templates, export-import, deleted or compare.
    public string ModelingTab(string tab)
    {
        return $"{Modeling}/{OneOf(tab, ModelingTabs, nameof(tab))}";
    }

    // The schema of an annotation (it applies to the annotation and its descendants).
    public string AnnotationSchema(string key)
    {
        return $"{Modeling}/schemas/annotation/{Segment(key)}";
    }

    // The schema of a type (its code, e.g. exe).
    public string TypeSchema(string type)
    {
        return $"{Modeling}/schemas/type/{Segment(type)}";
    }

    // The schema of an annotation for its descendants of a type (the type's code).
    public string DescendantSchema(string key, string type)
    {
        return $"{AnnotationSchema(key)}/type/{Segment(type)}";
    }

    // The template of a type (its code, e.g. exe).
    public string Template(string type)
    {
        return $"{Modeling}/templates/{Segment(type)}";
    }

    // Modeling's Deleted tab; cascaded also lists the descendants deleted with an annotation (?cascaded=1).
    public string Deleted(bool cascaded = false)
    {
        return WithQuery(ModelingTab("deleted"), ("cascaded", cascaded ? "1" : null));
    }

    // Modeling's Compare views tab; to is the view to compare with (?to=<view>).
    public string Compare(string? to = null)
    {
        return WithQuery(ModelingTab("compare"), ("to", to));
    }

    // An href that ends with the route (a path of the app, e.g. Routes.AnnotationSchema(key)), wherever the app is
    // served.
    public static Regex HrefEndingWith(string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        return new Regex($"/{Regex.Escape(route.Trim('/'))}$");
    }

    // An old page of the view, which redirects to its Modeling tab: schemas, templates, export-import, deleted or
    // compare; rest is what follows it, e.g. "exe" of templates/exe or "annotation/<key>" of schemas.
    public string OldViewPage(string page, string? rest = null)
    {
        var path = $"{ViewPath}/{OneOf(page, OldViewPages, nameof(page))}";
        return rest is null ? path : $"{path}/{rest.TrimStart('/')}";
    }

    // An old tab of an annotation's detail, which redirects to the configuration's detail: configuration, schema or
    // versions.
    public string OldAnnotationTab(string key, string tab)
    {
        return $"{Annotation(key)}/{OneOf(tab, OldDetailTabs, nameof(tab))}";
    }

    // An old tab of a graph node's detail, which redirects to the configuration's detail: configuration, schema or
    // versions.
    public string OldGraphTab(string key, string tab)
    {
        return $"{GraphNode(key)}/{OneOf(tab, OldDetailTabs, nameof(tab))}";
    }

    // An old page of the project, which redirects: secrets, certificates, invitations, usage or audit.
    public string OldProjectPage(string page)
    {
        return $"{ProjectPath}/{OneOf(page, OldProjectPages, nameof(page))}";
    }

    // One path segment: an annotation key, a type code, a machine id.
    private static string Segment(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Uri.EscapeDataString(value);
    }

    private static string OneOf(string value, string[] allowed, string paramName)
    {
        return allowed.Contains(value, StringComparer.Ordinal)
            ? value
            : throw new ArgumentException($"'{value}' is not one of {string.Join(", ", allowed)}.", paramName);
    }
}
