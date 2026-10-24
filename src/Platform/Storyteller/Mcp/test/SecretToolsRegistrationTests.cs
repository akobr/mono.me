using _42.Platform.Storyteller.Mcp.UnitTests.Support;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

// The tools that return secrets reach tools/list only when the deployment enables them.
public class SecretToolsRegistrationTests
{
    private static readonly string[] SecretToolNames = ["reveal_key_value", "get_resolved_configuration"];

    private static readonly string[] AlwaysListed = ["list_machines", "create_machine", "reset_machine", "list_key_values", "get_key_value", "diff_key_value"];

    [Fact]
    public async Task WithoutTheSetting_SecretToolsAreNotListed()
    {
        using var host = new McpTestHost();

        var names = await ListToolNamesAsync(host);

        names.ShouldNotContain(name => SecretToolNames.Contains(name));
        AlwaysListed.ShouldBeSubsetOf(names);
    }

    [Fact]
    public async Task SettingFalse_SecretToolsAreNotListed()
    {
        using var host = new McpTestHost(new Dictionary<string, string?> { ["Mcp:SecretTools:Enabled"] = "false" });

        var names = await ListToolNamesAsync(host);

        names.ShouldNotContain(name => SecretToolNames.Contains(name));
    }

    [Fact]
    public async Task WithTheSetting_SecretToolsAreListed()
    {
        using var host = new McpTestHost(new Dictionary<string, string?> { ["Mcp:SecretTools:Enabled"] = "true" });

        var names = await ListToolNamesAsync(host);

        SecretToolNames.ShouldBeSubsetOf(names);
        AlwaysListed.ShouldBeSubsetOf(names);
    }

    private static async Task<List<string>> ListToolNamesAsync(McpTestHost host)
    {
        await using var client = await host.ConnectAsync();
        var tools = await client.ListToolsAsync();
        return tools.Select(tool => tool.Name).ToList();
    }
}
