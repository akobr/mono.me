namespace _42.Platform.Storyteller.Accessing;

// An actor of Auth:TokenExchange:Actors: a party (the MCP server) that may exchange a person's token for a Storyteller
// user token that names it. It has no project, role or scope of its own; it acts within the person's roles, its Scopes
// and its restrictions.
public class TokenExchangeActorOptions
{
    // The client_id of its exchanges, and the act.sub and client_id of the tokens it gets.
    public string Id { get; set; } = string.Empty;

    // How it proves itself: exactly one method.
    public ActorAuthenticationOptions Authentication { get; set; } = new();

    // The subject token's aud must be one of these (the actor's resource URL, and for Entra ID its client id), so a
    // token a person gave another app cannot be exchanged by this actor.
    public string[] SubjectAudiences { get; set; } = [];

    // The clients (azp, appid or client_id) the subject token may be issued to. Empty: any client. With entries, every
    // other client is refused at the exchange, a subject token without a client too; checked at the exchange only.
    public string[] AllowedClientIds { get; set; } = [];

    // The clients that act with the actor's own policy only (IsTrustedClient, exact); every other client also gets
    // UntrustedClientRestrictions. Trust never removes a restriction of the actor itself.
    public string[] TrustedClientIds { get; set; } = [];

    // ActorRestrictions values added for every client outside TrustedClientIds, including a subject token that names no
    // client (GetRestrictions(clientId)).
    public string[] UntrustedClientRestrictions { get; set; } = [];

    // The most an issued token may carry; it must contain User.Impersonation. Empty: User.Impersonation (GetScopes).
    // Defaults stay out of the bound arrays, because binding appends to them.
    public string[] Scopes { get; set; } = [];

    // ActorRestrictions values on top of the default NoSecretReveal (GetRestrictions).
    public string[] Restrictions { get; set; } = [];

    // True removes NoSecretReveal from the defaults; naming NoSecretReveal in Restrictions as well is refused at start.
    public bool AllowSecrets { get; set; }

    // The scopes an issued token may carry at most.
    public IReadOnlyList<string> GetScopes()
    {
        return Scopes.Length == 0 ? [UserScopes.Impersonation] : Scopes.Distinct(StringComparer.Ordinal).ToArray();
    }

    // Restrictions, plus NoSecretReveal unless AllowSecrets. Every issued token carries them.
    public IReadOnlyList<string> GetRestrictions()
    {
        IEnumerable<string> restrictions = AllowSecrets ? Restrictions : [ActorRestrictions.NoSecretReveal, .. Restrictions];
        return restrictions.Distinct(StringComparer.Ordinal).ToArray();
    }

    // The restrictions of a token for the client: GetRestrictions(), plus UntrustedClientRestrictions unless the client is
    // trusted; distinct, in this order.
    public IReadOnlyList<string> GetRestrictions(string? clientId)
    {
        var restrictions = GetRestrictions();
        return IsTrustedClient(clientId)
            ? restrictions
            : restrictions.Concat(UntrustedClientRestrictions).Distinct(StringComparer.Ordinal).ToArray();
    }

    // False for null (a subject token that names no client). Entries are trimmed and compared ordinally: a trust list must
    // not widen, as a registrant may pick a case variant of a trusted id (Keycloak) and URL paths are case-sensitive.
    public bool IsTrustedClient(string? clientId)
    {
        return clientId is not null
            && TrustedClientIds.Any(trusted => !string.IsNullOrWhiteSpace(trusted) && string.Equals(trusted.Trim(), clientId, StringComparison.Ordinal));
    }
}
