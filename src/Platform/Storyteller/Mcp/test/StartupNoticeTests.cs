using _42.Platform.Storyteller.Mcp.Hosting;
using _42.Platform.Storyteller.Mcp.UnitTests.Support;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

// What the API must agree with is logged once at start: the audiences its actor lists, and under Entra ID the tenant.
public sealed class StartupNoticeTests : IDisposable
{
    private static readonly string Category = typeof(McpStartupNotice).FullName!;

    private readonly List<McpTestHost> _hosts = [];

    [Fact]
    public void EntraId_LogsBothAudiencesAndTheTenant()
    {
        var entries = Start();

        entries.ShouldBe(
        [
            $"Information {Category}: The API's Auth:TokenExchange:Actors entry 'mcp' must list these SubjectAudiences: {TestTokens.McpClientId}, {TestTokens.ResourceUrl}. ",
            $"Information {Category}: Under Entra ID this MCP server serves tenant {TestTokens.TenantId} only. Its sign-ins resolve to the Storyteller account {TestTokens.TenantId}:{{oid}}; a guest of the tenant resolves to its guest object there, not to the account of its home tenant. ",
        ]);
    }

    [Fact]
    public void EntraId_WithAnAudience_LogsItInsteadOfTheResourceUrl()
    {
        var entries = Start(new Dictionary<string, string?> { ["Mcp:Authentication:Audience"] = "api://storyteller-mcp" });

        entries.ShouldContain($"Information {Category}: The API's Auth:TokenExchange:Actors entry 'mcp' must list these SubjectAudiences: {TestTokens.McpClientId}, api://storyteller-mcp. ");
    }

    [Theory]
    [InlineData("AuthKit")]
    [InlineData("Keycloak")]
    public void IssuerProvider_LogsTheAudienceOnly(string provider)
    {
        var entries = Start(new Dictionary<string, string?> { ["Mcp:Provider"] = provider });

        entries.ShouldBe([$"Information {Category}: The API's Auth:TokenExchange:Actors entry 'mcp' must list these SubjectAudiences: {TestTokens.ResourceUrl}. "]);
    }

    public void Dispose()
    {
        foreach (var host in _hosts)
        {
            host.Dispose();
        }
    }

    private List<string> Start(IDictionary<string, string?>? settings = null)
    {
        var host = new McpTestHost(settings);
        _hosts.Add(host);

        // Creating a client starts the host and its hosted services.
        using var client = host.CreateClient();

        return host.Logs.Entries.Where(entry => entry.Contains($" {Category}: ", StringComparison.Ordinal)).ToList();
    }
}
