namespace _42.Platform.Storyteller.Accessing;

// The actor that acts for the person of a Storyteller user token (its act.sub, an actor of Auth:TokenExchange:Actors),
// the token's restrictions plus those of the actor's current settings (ActorRestrictions values; empty when neither
// has any), and the client app the person used with the actor (the token's subject_client_id: the subject token's azp,
// appid or client_id; null when it named none, and for a token issued before the claim).
public sealed record BearerDelegation(string ActorId, IReadOnlyList<string> Restrictions, string? ClientId = null);
