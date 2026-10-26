using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Mcp.Hosting;

// Notes once at start what the Storyteller API must agree with, since neither side can check the other's settings:
// - the audiences this server accepts, which the API's actor must list in SubjectAudiences, or every exchange is refused
//   with invalid_grant;
// - under Entra ID, the one tenant it serves: its sign-ins resolve to {TenantId}:{oid}, so a B2B guest of the tenant
//   reaches the account of its guest object, not the one registered through its home tenant.
// Logging only, without I/O. The options are read when the host starts, after their validation.
public sealed class McpStartupNotice(IOptions<McpHostOptions> options, ILogger<McpStartupNotice> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var hostOptions = options.Value;
        var settings = McpAuthSettings.From(hostOptions);

        logger.LogInformation(
            "The API's Auth:TokenExchange:Actors entry '{ActorId}' must list these SubjectAudiences: {Audiences}.",
            hostOptions.Actor.Id,
            string.Join(", ", settings.ValidAudiences));

        if (settings.Provider == McpIdentityProvider.EntraId)
        {
            logger.LogInformation(
                "Under Entra ID this MCP server serves tenant {TenantId} only. Its sign-ins resolve to the Storyteller account {AccountIdForm}; a guest of the tenant resolves to its guest object there, not to the account of its home tenant.",
                settings.TenantId,
                $"{settings.TenantId}:{{oid}}");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
