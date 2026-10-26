namespace _42.Platform.Storyteller.Mcp.Hosting;

// The identity provider people sign in to the MCP server with; the same as the API's Auth:Provider.
public enum McpIdentityProvider
{
    EntraId = 0,
    AuthKit,
    Keycloak,
}
