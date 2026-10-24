namespace _42.Platform.Storyteller.Mcp.Understanding;

// A key a binding reads from a source. The Values store answers under the names default, public and secret
// (Binding.Core KeyValueBindingSource); any other source is registered by the deployment, for example a Key Vault.
public sealed record BindingReference(string Key, string Source)
{
    public bool IsValuesStore => BindingExpressions.ValuesStoreSources.Contains(Source);
}
