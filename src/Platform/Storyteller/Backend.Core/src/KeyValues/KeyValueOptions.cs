namespace _42.Platform.Storyteller.KeyValues;

// Section "KeyValues".
public class KeyValueOptions
{
    public const string SectionName = "KeyValues";

    // How long a version that is no longer current is kept.
    public TimeSpan VersionRetention { get; set; } = TimeSpan.FromDays(365);

    // How long a soft-deleted key is kept.
    public TimeSpan DeletedRetention { get; set; } = TimeSpan.FromDays(90);

    // How long audit events are kept; a little over a year, for yearly audits.
    public TimeSpan AuditRetention { get; set; } = TimeSpan.FromDays(400);

    public int MaxRecipients { get; set; } = 20;

    public KeyValueProtectionOptions Protection { get; set; } = new();

    public KeyValueUsageOptions Usage { get; set; } = new();

    // List prices per priced item, for the estimates of the usage report; empty means no estimates.
    public Dictionary<string, decimal> Pricing { get; set; } = new(StringComparer.Ordinal);
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

    // The HSM level: a Key Vault Premium vault shared by the projects, with RSA-HSM keys.
    public KeyVaultKeyOptions Hsm { get; set; } = new() { VaultName = "premium", CacheDuration = TimeSpan.FromMinutes(5) };

    // Organizations with a dedicated Managed HSM: organization -> pool URI.
    public Dictionary<string, string> OrganizationManagedHsms { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public CustomerKeyOptions Byok { get; set; } = new();
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

// Section "KeyValues:Protection:Byok": the cross-tenant identity that wraps with the customer's key.
public class CustomerKeyOptions
{
    // The multi-tenant app registration the customer consents to.
    public string? ClientId { get; set; }

    // The user-assigned managed identity that is the app's federated credential.
    public string? ManagedIdentityClientId { get; set; }

    // The longest a revoked customer key keeps working, through the plaintext cache.
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    public string RequiredRole { get; set; } = "Key Vault Crypto Service Encryption User";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);
}

// Section "KeyValues:Usage".
public class KeyValueUsageOptions
{
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(60);

    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(400);
}
