namespace _42.Platform.Storyteller.Sdk.Credentials;

public enum StorytellerAuthenticationFailureReason
{
    // Missing or conflicting variables, an unreadable file, a missing binding, or a URL that breaks the local URL rule.
    Configuration = 0,

    // The token or discovery endpoint, or the source of a platform token, refused the request (a redirect, or a 4xx other
    // than 408 and 429, and other than 404 and 410 of IMDS) or gave no token; a provider failed with another exception
    // than a timeout or a network error; or discovery is not AuthKit.
    Rejected,

    // A network error, a timeout, an open circuit, or 408, 429 or 5xx, after retries; 404 or 410 of IMDS, which it
    // answers while it is updating, without a retry; worth trying again later.
    Unavailable,
}
