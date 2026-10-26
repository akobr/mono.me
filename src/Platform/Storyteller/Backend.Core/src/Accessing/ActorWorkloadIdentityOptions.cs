namespace _42.Platform.Storyteller.Accessing;

// The platform identity an actor proves itself with, through actor_token.
// An Azure managed identity's token takes its version from the requestedAccessTokenVersion of the app registration it
// is requested for, and Issuer and Audience must be the pair of that version, exactly as the token carries them:
// - v1 (the default): Issuer https://sts.windows.net/{tid}/ (with the trailing slash), Audience the App ID URI as
//   requested, for example api://storyteller-token-exchange.
// - v2: Issuer https://login.microsoftonline.com/{tid}/v2.0, Audience the app's client id (a GUID), never its App ID URI.
public class ActorWorkloadIdentityOptions
{
    // The OIDC issuer of the identity, compared ordinally with the token's iss: for a managed identity
    // https://sts.windows.net/{tid}/ (v1 tokens) or https://login.microsoftonline.com/{tid}/v2.0 (v2 tokens).
    public string Issuer { get; set; } = string.Empty;

    // The sub of the identity's tokens, for example the object id of a managed identity.
    public string Subject { get; set; } = string.Empty;

    // The aud the identity's tokens carry: for a managed identity the App ID URI as requested (v1 tokens, for example
    // api://storyteller-token-exchange) or the app's client id (v2 tokens).
    public string Audience { get; set; } = string.Empty;

    // Null: the keys come from the issuer's OpenID discovery, {Issuer}/.well-known/openid-configuration. With the v1
    // issuer, set https://login.microsoftonline.com/{tid}/discovery/keys if that discovery path is not wanted.
    public string? JwksUri { get; set; }
}
