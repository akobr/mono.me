namespace _42.Platform.Storyteller.Accessing.Federation;

// Values of the RFC 8693 token exchange and the RFC 6749 errors of the token endpoint.
public static class OAuthTokenExchange
{
    public const string GrantType = "urn:ietf:params:oauth:grant-type:token-exchange";

    // token_type of the response.
    public const string BearerTokenType = "Bearer";

    public static class TokenTypes
    {
        public const string AccessToken = "urn:ietf:params:oauth:token-type:access_token";
        public const string IdToken = "urn:ietf:params:oauth:token-type:id_token";
        public const string Jwt = "urn:ietf:params:oauth:token-type:jwt";
    }

    public static class Errors
    {
        public const string InvalidRequest = "invalid_request";
        public const string UnsupportedGrantType = "unsupported_grant_type";
        public const string InvalidScope = "invalid_scope";
        public const string InvalidTarget = "invalid_target";

        // An extension of RFC 6749 section 5.2 on the token endpoint; answered with 503 and Retry-After.
        public const string TemporarilyUnavailable = "temporarily_unavailable";
    }
}
