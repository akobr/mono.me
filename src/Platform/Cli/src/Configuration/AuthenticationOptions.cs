namespace _42.Platform.Cli.Configuration;

public class AuthenticationOptions
{
    // EntraId or AuthKit. Empty asks the server (GET v1/auth/configuration) and caches the answer.
    public string? Provider { get; set; }

    // Entra ID only.
    public string? TenantId { get; set; }

    // Entra ID application ID or AuthKit client_… ID.
    public string? ClientId { get; set; }

    // AuthKit only. Defaults to https://api.workos.com.
    public string? AuthKitApiBaseUrl { get; set; }
}
