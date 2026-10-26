namespace _42.Platform.Storyteller.Accessing.Federation;

// Values and rules of the RFC 8693 token exchange and the RFC 6749 errors of the token endpoint, shared by the machine
// and the user token endpoints.
public static class OAuthTokenExchange
{
    public const string GrantType = "urn:ietf:params:oauth:grant-type:token-exchange";

    // token_type of the response.
    public const string BearerTokenType = "Bearer";

    // RFC 6749 section 3.3: scope-tokens of %x21 / %x23-5B / %x5D-7E, separated by single spaces.
    public static bool IsScopeSyntax(string scope)
    {
        return scope.Split(' ').All(token => token.Length > 0
            && token.All(character => character is '\x21' or (>= '\x23' and <= '\x5B') or (>= '\x5D' and <= '\x7E')));
    }

    public static class TokenTypes
    {
        public const string AccessToken = "urn:ietf:params:oauth:token-type:access_token";
        public const string IdToken = "urn:ietf:params:oauth:token-type:id_token";
        public const string Jwt = "urn:ietf:params:oauth:token-type:jwt";
    }

    public static class Errors
    {
        public const string InvalidRequest = "invalid_request";

        // The actor of a user token exchange is unknown or its proof is not valid.
        public const string InvalidClient = "invalid_client";

        // The subject token of a user token exchange is not accepted, or names no account.
        public const string InvalidGrant = "invalid_grant";

        public const string UnsupportedGrantType = "unsupported_grant_type";
        public const string InvalidScope = "invalid_scope";
        public const string InvalidTarget = "invalid_target";

        // An extension of RFC 6749 section 5.2 on the token endpoint; answered with 503 and Retry-After.
        public const string TemporarilyUnavailable = "temporarily_unavailable";
    }
}
