using System.Collections.Frozen;

namespace _42.Platform.Storyteller.Mcp.Tools;

// The tools that return secret values or have the API evaluate them (SecretTools, BindingCheckTools). They exist only
// with Mcp:SecretTools:Enabled, and while the API withholds secrets from the MCP server's actor (NoSecretReveal)
// SecretToolsFilter hides them from tools/list and StorytellerCallFilter refuses them.
public static class SecretToolNames
{
    public const string RevealKeyValue = "reveal_key_value";
    public const string GetResolvedConfiguration = "get_resolved_configuration";
    public const string CheckConfigurationBindings = "check_configuration_bindings";

    public static readonly FrozenSet<string> All = new[] { RevealKeyValue, GetResolvedConfiguration, CheckConfigurationBindings }.ToFrozenSet(StringComparer.Ordinal);

    public static bool Contains(string? name)
    {
        return name is not null && All.Contains(name);
    }
}
