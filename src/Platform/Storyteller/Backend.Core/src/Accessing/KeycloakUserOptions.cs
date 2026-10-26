namespace _42.Platform.Storyteller.Accessing;

// Keycloak users (Auth:Keycloak, when Auth:Provider is Keycloak). Not the root Keycloak section of machine access,
// which names a server, a realm and admin credentials.
public class KeycloakUserOptions
{
    // The realm issuer, {server}/realms/{realm}. User tokens are checked against it exactly (a trailing '/' aside), and
    // their keys come from its OpenID metadata.
    public string Issuer { get; set; } = string.Empty;

    // The aud a user token must carry; an audience mapper on the sign-in clients adds it (Keycloak's default is account).
    public string Audience { get; set; } = string.Empty;

    // The public client that clients such as sform sign in with, published by v1/auth/configuration. Optional; when
    // set, one of AllowedClientIds.
    public string? ClientId { get; set; }

    // The clients (azp) whose user tokens are accepted. At least one. The audience alone is a weak gate in Keycloak: the
    // default roles scope's audience-resolve mapper adds a client to the aud of every client's token once the user holds
    // a role of that client.
    public string[] AllowedClientIds { get; set; } = [];

    // Added to every user token. In practice only User.Impersonation: users are authorized by their role, and data
    // scopes in a user token are ignored.
    public string[] DefaultUserScopes { get; set; } = [];

    // A realm role, or a client role as {client}:{role}, => the scope strings it adds (User.Impersonation in practice).
    // Keys contain ':'; AddUserAuthenticationOptions reads the section so they bind as one key.
    public Dictionary<string, string> RoleMap { get; set; } = new();

    public string GetIssuer()
    {
        return Issuer.Trim().TrimEnd('/');
    }
}
