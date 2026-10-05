namespace _42.Platform.Storyteller;

// Entra ID machine access: app registrations are created in this directory, and machines get
// their tokens from it. The API application and its app roles come from the Auth section.
public class AzureAdMachineAccessOptions
{
    public const string SectionName = "MachineAuth:AzureAd";

    public string? TenantId { get; set; }

    public string GetTokenEndpoint()
    {
        return $"https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/token";
    }
}
