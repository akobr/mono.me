namespace _42.Platform.UI.Admin.AutomationTests.Data;

// The Key-Values section's generators.
public sealed partial class PlatformData
{
    // A Key Vault secret reference "kv://at-vault/at-ref-<hex4>" (vault and secret names may hold '-'), the value of a
    // public pair of the content type "Secret reference"; nothing resolves it.
    public string SecretReference()
    {
        return $"kv://{Prefix}-vault/{Prefix}-ref-{Data.Hex(HexLength)}";
    }
}
