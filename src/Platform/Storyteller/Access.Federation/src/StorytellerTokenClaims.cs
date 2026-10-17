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
}
