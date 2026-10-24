namespace _42.Platform.Storyteller.Mcp.IntegrationTests;

// A real tenant. Every value comes from an environment variable; without them the tests do nothing.
// - MCP_TEST_TENANT_ID, MCP_TEST_CLIENT_ID, MCP_TEST_CLIENT_SECRET: the MCP app registration and its secret.
// - MCP_TEST_STORYTELLER_SCOPE: the delegated scope of the API, for example api://{ApiClientId}/User.Impersonation.
// - MCP_TEST_USER_TOKEN: a token of the person for the MCP app (as an MCP client gets it).
// - MCP_TEST_SFORM_TOKEN: a token of the same person for the API, as sform gets it (sform auth token or the MSAL cache).
internal sealed record EntraTestSettings(
    string TenantId,
    string ClientId,
    string ClientSecret,
    string StorytellerScope,
    string UserToken,
    string SformToken)
{
    public const string Variables =
        "MCP_TEST_TENANT_ID, MCP_TEST_CLIENT_ID, MCP_TEST_CLIENT_SECRET, MCP_TEST_STORYTELLER_SCOPE, MCP_TEST_USER_TOKEN, MCP_TEST_SFORM_TOKEN";

    public static EntraTestSettings? TryRead()
    {
        var tenantId = Read("MCP_TEST_TENANT_ID");
        var clientId = Read("MCP_TEST_CLIENT_ID");
        var clientSecret = Read("MCP_TEST_CLIENT_SECRET");
        var scope = Read("MCP_TEST_STORYTELLER_SCOPE");
        var userToken = Read("MCP_TEST_USER_TOKEN");
        var sformToken = Read("MCP_TEST_SFORM_TOKEN");

        return tenantId is null || clientId is null || clientSecret is null || scope is null || userToken is null || sformToken is null
            ? null
            : new EntraTestSettings(tenantId, clientId, clientSecret, scope, userToken, sformToken);
    }

    private static string? Read(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
