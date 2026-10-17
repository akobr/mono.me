namespace _42.Platform.Storyteller.Accessing;

// Workload identity federation of Federated machines, section MachineAuth:Federation.
public class FederationOptions
{
    public const string SectionName = "MachineAuth:Federation";

    public const int MinTokenLifetimeMinutes = 5;
    public const int MaxTokenLifetimeMinutes = 60;
    public const int MaxClockSkewSeconds = 120;
    public const int MinSubjectTokenBytes = 1024;
    public const int MaxSubjectTokenBytesLimit = 32 * 1024;
    public const int MaxMetadataRefreshMinutes = 24 * 60;

    // `iss` of Storyteller tokens. Empty: urn:storyteller:{MachineAuth:TrustDomain}.
    public string Issuer { get; set; } = string.Empty;

    // `aud` of Storyteller tokens and the only accepted `audience`/`resource` of an exchange.
    // Empty: urn:storyteller:{MachineAuth:TrustDomain}:api.
    public string Audience { get; set; } = string.Empty;

    // The public base URL of the API (https://host/api), used for TokenEndpoint. Null: the scheme and host
    // of the request URL (never X-Forwarded-Host).
    public string? PublicBaseUrl { get; set; }

    public int TokenLifetimeMinutes { get; set; } = 15;

    // Skew for platform and Storyteller tokens.
    public int ClockSkewSeconds { get; set; } = 30;

    // Larger platform tokens are refused before parsing.
    public int MaxSubjectTokenBytes { get; set; } = 16 * 1024;

    // Age after which discovery documents and key sets are fetched again.
    public int MetadataRefreshMinutes { get; set; } = 60;

    // Create the signing key on first issuance when none is stored.
    public bool SigningKeyAutoBootstrap { get; set; } = true;

    // Lifts the https, port 443, DNS host and public address limits of issuer URLs (development, tests).
    public bool AllowPrivateNetworks { get; set; }

    public static string GetDefaultIssuer(string trustDomain) => $"urn:storyteller:{trustDomain}";

    public static string GetDefaultAudience(string trustDomain) => $"urn:storyteller:{trustDomain}:api";

    // Fills an empty Issuer and Audience from the trust domain of the machine certificates.
    public void ApplyTrustDomainDefaults(string trustDomain)
    {
        if (string.IsNullOrWhiteSpace(Issuer))
        {
            Issuer = GetDefaultIssuer(trustDomain);
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            Audience = GetDefaultAudience(trustDomain);
        }
    }
}
