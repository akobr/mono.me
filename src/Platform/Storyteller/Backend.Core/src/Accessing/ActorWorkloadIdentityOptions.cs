namespace _42.Platform.Storyteller.Accessing;

// The platform identity an actor proves itself with, through actor_token.
public class ActorWorkloadIdentityOptions
{
    // The OIDC issuer of the identity, for example https://login.microsoftonline.com/{tid}/v2.0.
    public string Issuer { get; set; } = string.Empty;

    // The sub of the identity's tokens, for example the object id of a managed identity.
    public string Subject { get; set; } = string.Empty;

    // The aud the identity's tokens are requested for, for example api://storyteller-token-exchange.
    public string Audience { get; set; } = string.Empty;

    // Null: the keys come from the issuer's OpenID discovery.
    public string? JwksUri { get; set; }
}
