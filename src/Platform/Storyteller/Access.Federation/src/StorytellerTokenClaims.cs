namespace _42.Platform.Storyteller;

// Claims and header values of Storyteller tokens (RFC 9068 access tokens).
public static class StorytellerTokenClaims
{
    public const string TokenType = "at+jwt";
    public const string MediaTokenType = "application/at+jwt";

    public const string ClientId = "client_id";
    public const string Scope = "scope";
    public const string Organization = "organization";
    public const string Project = "project";
    public const string AnnotationKey = "annotation_key";

    // For audit and logs: the trusted issuer name and the platform `sub` the token was exchanged for.
    public const string FederationIssuer = "federation_issuer";
    public const string FederationSubject = "federation_subject";

    // User tokens of the user token exchange carry token_use "user"; machine tokens carry no token_use.
    public const string TokenUse = "token_use";
    public const string UserTokenUse = "user";

    // RFC 8693 section 4.1: a JSON object whose sub is the actor id. Never nested.
    public const string Act = "act";

    // The actor's restrictions (ActorRestrictions), a JSON array of strings, empty when it has none.
    public const string Restrictions = "restrictions";

    // When the person signed in, in Unix seconds, when the subject token said so.
    public const string AuthTime = "auth_time";
}
