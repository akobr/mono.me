namespace _42.Platform.Storyteller.Sdk.Credentials;

public enum StorytellerAuthenticationFailureReason
{
    // Missing or conflicting variables, an unreadable file, a missing binding, or a URL that breaks the local URL rule.
    Configuration = 0,

    // The token or discovery endpoint refused the request (a 4xx other than 408 and 429), or discovery is not AuthKit.
    Rejected,

    // A network error, a timeout, an open circuit, or 408, 429 or 5xx, after retries; worth trying again later.
    Unavailable,
}
