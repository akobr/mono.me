using System.ComponentModel;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace _42.Platform.Storyteller.Mcp.Tools;

[McpServerToolType]
public sealed class DiscoveryTools(Sdk.IAccessApiClient access, Sdk.IAnnotationsApiClient annotations)
{
    public const string MissingAccountMessage =
        "No Storyteller account matches this sign-in. If the person has an account through sform or the admin UI, the "
        + "identity (sub) of the MCP sign-in may differ from it; see docs/Platform/Storyteller/mcp.md, \"Identity\". "
        + "Otherwise the person registers first, with sform or the admin UI.";

    [McpServerTool(Name = "get_my_account", Title = "Get my account", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Returns the signed-in person's Storyteller account: id, user name, name and the role per organization and project (AccessMap).")]
    public async Task<CallToolResult> GetMyAccount(CancellationToken cancellationToken)
    {
        try
        {
            return ToolResults.Json(await access.GetAccountAsync(cancellationToken));
        }
        catch (Sdk.ApiException exception) when (exception.StatusCode == 404)
        {
            return ToolResults.Error(MissingAccountMessage, new { Status = 404 });
        }
    }

    [McpServerTool(Name = "list_projects", Title = "List my projects", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists the projects the signed-in person is a member of: the key \"organization.project\", the person's role and the description.")]
    public async Task<CallToolResult> ListProjects(CancellationToken cancellationToken)
    {
        return ToolResults.Json(await access.GetProjectsAsync(cancellationToken));
    }

    [McpServerTool(Name = "list_views", Title = "List views", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists the registered views of a project, \"default\" first. Views are isolated catalogs: nothing is inherited between them. A view also exists implicitly once something is written into it.")]
    public async Task<CallToolResult> ListViews(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        CancellationToken cancellationToken)
    {
        return ToolResults.Json(await annotations.GetViewsAsync(organization, project, null, cancellationToken));
    }
}
