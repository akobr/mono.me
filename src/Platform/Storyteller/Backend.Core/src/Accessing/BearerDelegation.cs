namespace _42.Platform.Storyteller.Accessing;

// The actor that acts for the person of a Storyteller user token (its act.sub, an actor of Auth:TokenExchange:Actors),
// and the token's restrictions plus those of the actor's current settings (ActorRestrictions values; empty when neither
// has any).
public sealed record BearerDelegation(string ActorId, IReadOnlyList<string> Restrictions);
