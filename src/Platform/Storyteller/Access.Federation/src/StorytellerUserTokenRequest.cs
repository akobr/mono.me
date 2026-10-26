namespace _42.Platform.Storyteller;

// What a Storyteller user token says after a user token exchange: the person, the actor that acts for them, and what the
// actor may do. No name or email: the API reads them from the account.
public sealed record StorytellerUserTokenRequest
{
    // UserIdentity.GetAccountId of the subject token, an existing account.
    public required string AccountId { get; init; }

    // The act.sub and client_id of the token.
    public required string ActorId { get; init; }

    // The granted scopes; they contain User.Impersonation.
    public required IReadOnlyList<string> Scopes { get; init; }

    // ActorRestrictions values; empty when the actor has none.
    public required IReadOnlyList<string> Restrictions { get; init; }

    public DateTimeOffset? AuthTime { get; init; }

    // Auth:TokenExchange:TokenLifetimeMinutes, at most TokenExchangeOptions.MaxTokenLifetimeMinutes.
    public required TimeSpan MaxLifetime { get; init; }

    // The subject token's exp: the token never outlives it.
    public required DateTimeOffset NotAfter { get; init; }
}
