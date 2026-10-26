namespace _42.Platform.Storyteller.Mcp.Hosting;

// Section Mcp:Actor: how the MCP server takes part in the Storyteller API's user token exchange. It is the actor of
// Auth:TokenExchange:Actors whose Id it names, and proves itself with exactly one credential.
public sealed class McpActorOptions
{
    // The client_id of the exchange: the Id of the API's actor, act.sub of the issued tokens. For example mcp.
    public string Id { get; set; } = string.Empty;

    public McpWorkloadIdentityOptions? WorkloadIdentity { get; set; }

    public McpClientAssertionKeyOptions? ClientAssertionKey { get; set; }

    // Development only: the secret whose SHA-256 hash is the actor's ClientSecretHash, sent as client_secret.
    public string? ClientSecret { get; set; }

    internal bool HasWorkloadIdentity => WorkloadIdentity?.IsSet == true;

    internal bool HasClientAssertionKey => ClientAssertionKey?.IsSet == true;

    internal bool HasClientSecret => !string.IsNullOrWhiteSpace(ClientSecret);
}
