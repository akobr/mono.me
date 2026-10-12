namespace _42.Platform.Storyteller.KeyValues;

// Section "KeyValues".
public class KeyValueOptions
{
    public const string SectionName = "KeyValues";

    // How long a version that is no longer current is kept.
    public TimeSpan VersionRetention { get; set; } = TimeSpan.FromDays(365);

    // How long a soft-deleted key is kept.
    public TimeSpan DeletedRetention { get; set; } = TimeSpan.FromDays(90);

    public KeyValueProtectionOptions Protection { get; set; } = new();
}

// Section "KeyValues:Protection".
public class KeyValueProtectionOptions
{
    // Talk to a local Key Vault emulator (Lowkey Vault): no real credentials, any server certificate. Never in production.
    public bool Emulator { get; set; }

    // Parallel wrap and unwrap calls per instance.
    public int MaxConcurrentKeyOperations { get; set; } = 8;

    // Bytes of decrypted plaintext kept in memory across all levels.
    public long CacheSizeLimit { get; set; } = 64L * 1024 * 1024;

    public KeyVaultKeyOptions Standard { get; set; } = new();
}

// One level backed by a key-encryption key in Key Vault.
public class KeyVaultKeyOptions
{
    // A key of the AzureKeyVaults section.
    public string VaultName { get; set; } = "default";

    public string KeyNamePrefix { get; set; } = "kek-";

    public int KeySize { get; set; } = 2048;

    public TimeSpan RotationInterval { get; set; } = TimeSpan.FromDays(365);

    // How long a decrypted value stays in memory. Zero turns the cache off.
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(10);
}
