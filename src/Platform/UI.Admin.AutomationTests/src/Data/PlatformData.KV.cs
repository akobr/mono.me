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

    // A prefix of keys of the values store, "at_<runId>.<word>_<hex4>" in lower case (an .env import lower-cases its
    // keys): the keys of an import are "<prefix>.<name>", so a Replace "Only under prefix" touches only them.
    public string KeyValuePrefix()
    {
        return KeyValueKey();
    }

    // The .env form of a key, as Export writes it and Import reads it back: '.' becomes "__", upper case
    // (db.password is DB__PASSWORD).
    public static string ToEnvKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return key.Replace(".", "__", StringComparison.Ordinal).ToUpperInvariant();
    }
}
