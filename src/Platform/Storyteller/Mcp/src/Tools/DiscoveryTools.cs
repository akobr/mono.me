using System.ComponentModel;

using _42.Platform.Storyteller.Mcp.Authentication;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace _42.Platform.Storyteller.Mcp.Tools;

[McpServerToolType]
public sealed class DiscoveryTools(Sdk.IAccessApiClient access, Sdk.IAnnotationsApiClient annotations)
{
    // The same text as the token exchange's refusal of a sign-in without an account.
    public const string MissingAccountMessage = StorytellerSignInRefusedException.NoAccountMessage;

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
            // With an exchanged token the API answers 401 for a missing account (BearerAuthenticationMiddleware) and the
            // exchange refuses a sign-in without one (NoAccount), so this 404 only guards a race in which the account
            // disappears between the middleware's read and the handler's. Kept as defence in depth.
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
        return ToolResults.Json(await annotations.GetViewsAsync(organization, project, cancellationToken: cancellationToken));
    }
}
