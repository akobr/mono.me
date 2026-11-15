namespace _42.Platform.Storyteller.Sdk.Credentials;

// Audience: the token endpoint, as the GitHub runner gets it; a provider whose rule names another audience ignores it.
// ClientId: the machine id of the exchange.
public sealed record StorytellerSubjectTokenRequest(string Audience, string ClientId);
