namespace _42.Platform.Storyteller.Accessing;

public class UserAuthenticationOptions
{
    public const string SectionName = "Auth";

    public IdentityProviderKind Provider { get; set; } = IdentityProviderKind.EntraId;

    // Legacy flat keys stay valid: Auth:TenantId / Auth:ClientId / Auth:AppRoles:* bind here.
    public string? TenantId { get; set; }

    public string? ClientId { get; set; }

    public Dictionary<string, string> AppRoles { get; set; } = new();

    public AuthKitOptions AuthKit { get; set; } = new();
}
