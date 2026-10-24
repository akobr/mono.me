namespace _42.Platform.Storyteller.Mcp.Tools;

// Descriptions shared by the parameters of many tools.
public static class ToolText
{
    public const string DefaultView = "default";

    public const string Organization = "The organization, the first part of a project key \"organization.project\" from list_projects.";

    public const string Project = "The project, the second part of a project key \"organization.project\" from list_projects.";

    public const string View = "The view, an isolated catalog of the project; \"default\" unless the person names another (list_views).";

    public const string AnnotationKey =
        "An annotation key {typeCode}.{name}[.{name}...], for example sbt.northwind or exe.northwind.invoicing.production.";

    public const string AnnotationType = "An annotation type code: rst, unt, sbt, usg, cnt, exe or uxe.";

    public const string Limit = "How many items to return, 1 to 200; default 50.";

    public const string ContinuationToken = "The ContinuationToken of the previous result, to get the next page; omit it for the first page.";

    public const string Version = "A stored version number, from the matching list_*_versions tool.";

    public const string VersionFrom = "The version to compare with; omit it to compare with the previous version.";
}
