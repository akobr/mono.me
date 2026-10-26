namespace _42.Platform.Storyteller.Mcp.Hosting;

public sealed class McpSecretToolsOptions
{
    // Registers reveal_key_value, get_resolved_configuration and check_configuration_bindings (Tools.SecretToolNames).
    // A local switch that can only remove tools: with it on, the server still hides and refuses them while the API
    // withholds secrets from its actor (NoSecretReveal, the default of every actor), and the API refuses secret reads
    // (403 ActorRestricted) to such an actor whatever this says.
    public bool Enabled { get; set; }
}
