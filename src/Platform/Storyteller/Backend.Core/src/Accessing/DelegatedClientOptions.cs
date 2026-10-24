namespace _42.Platform.Storyteller.Accessing;

// A client app whose delegated tokens act for the signed-in user (Entra ID), for example the MCP server that
// calls the API on behalf of a person. Pinned to one tenant: the same app from another tenant stays a machine.
public class DelegatedClientOptions
{
    public string ClientId { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;
}
