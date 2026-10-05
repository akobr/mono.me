namespace _42.Platform.Storyteller;

// Keycloak machine access. The keys are the Keycloak:* settings the service read from the
// environment before.
public class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    public string ServerUrl { get; set; } = "http://localhost:8080";

    // The realm where machine clients are created and their tokens issued.
    public string Realm { get; set; } = "storyteller";

    public string AdminRealm { get; set; } = "master";

    public string AdminClientId { get; set; } = "admin-cli";

    // Either a client secret (client_credentials) or a username and password for the admin token.
    public string? AdminClientSecret { get; set; }

    public string? AdminUsername { get; set; }

    public string? AdminPassword { get; set; }

    // Added to the aud of every machine token by an audience mapper on the client; the API requires it.
    public string Audience { get; set; } = "storyteller";

    public string GetIssuer()
    {
        return $"{ServerUrl.TrimEnd('/')}/realms/{Realm}";
    }

    public string GetTokenEndpoint()
    {
        return $"{GetIssuer()}/protocol/openid-connect/token";
    }
}
