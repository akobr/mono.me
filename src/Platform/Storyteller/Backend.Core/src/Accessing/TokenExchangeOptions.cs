namespace _42.Platform.Storyteller.Accessing;

// The user token exchange (RFC 8693, section Auth:TokenExchange): an actor such as the MCP server exchanges a user token
// of the deployment's identity provider for a Storyteller user token that names the actor. Bound and checked at start by
// AddUserAuthenticationOptions.
public class TokenExchangeOptions
{
    public const string SectionName = "Auth:TokenExchange";

    public const int MinTokenLifetimeMinutes = 5;
    public const int MaxTokenLifetimeMinutes = 15;
    public const int MaxExchangesPerSubjectTokenPerMinuteLimit = 60;
    public const int MinActorTokenAgeMinutes = 5;
    public const int MaxActorTokenAgeMinutesLimit = 1440;

    // Off: v1/access/token answers unsupported_grant_type, and the actors are not trusted.
    public bool Enabled { get; set; }

    // Issuers of the deployment's provider whose user tokens may be exchanged, besides the API's own (AuthKit only: the
    // AuthKit domain that issues MCP sign-in tokens). Accepted for the exchange, never as bearer tokens of API routes.
    public List<TokenExchangeIssuerOptions> AdditionalIssuers { get; set; } = [];

    public List<TokenExchangeActorOptions> Actors { get; set; } = [];

    // Clients (azp, appid or client_id of the subject token) blocked for every actor, even one that allows or trusts them:
    // the exchange refuses them, and their live tokens get 401. The kill switch for clients with stable ids (Client ID
    // Metadata Document URLs, Entra ID apps, Keycloak clients); a dynamically registered client registers again under a
    // new id, which an actor's AllowedClientIds holds off. Exchange-wide, so an entry never makes the policies of actors
    // that share an audience differ.
    public string[] DeniedClientIds { get; set; } = [];

    // The longest lifetime of an issued token; never longer than the subject token's exp.
    public int TokenLifetimeMinutes { get; set; } = 10;

    // Per actor and subject token (jti, else the hash of the token), in memory per instance.
    public int MaxExchangesPerSubjectTokenPerMinute { get; set; } = 10;

    // The longest time since the iat of a workload identity actor_token; an older one is refused whatever its exp.
    public int MaxActorTokenAgeMinutes { get; set; } = MaxActorTokenAgeMinutesLimit;

    // True when one of the audiences (a token's aud) is in the SubjectAudiences of an actor, enabled or not: the token was
    // issued to the actor for the exchange, and presenting it as a bearer token is token passthrough. Compared as the start
    // check compares audiences: trimmed, without a trailing '/', ignoring case.
    public bool IsIssuedToAnActor(IEnumerable<string> audiences)
    {
        ArgumentNullException.ThrowIfNull(audiences);

        var actorAudiences = Actors
            .SelectMany(actor => actor.SubjectAudiences)
            .Select(NormalizeAudience)
            .Where(audience => audience.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return actorAudiences.Count > 0
            && audiences.Any(audience => actorAudiences.Contains(NormalizeAudience(audience)));
    }

    // False for null (a subject token that names no client). Entries are trimmed and compared ignoring case: a wider match
    // is the safe direction for a deny list.
    public bool IsDeniedClient(string? clientId)
    {
        return clientId is not null
            && DeniedClientIds.Any(denied => !string.IsNullOrWhiteSpace(denied) && string.Equals(denied.Trim(), clientId, StringComparison.OrdinalIgnoreCase));
    }

    // Trimmed and without a trailing '/'; compare the result ignoring case. Shared with the start check of actors that
    // list the same audience.
    internal static string NormalizeAudience(string? audience)
    {
        return audience?.Trim().TrimEnd('/') ?? string.Empty;
    }
}
