namespace _42.Platform.Cli.Authentication;

// The values of an RFC 8693 token exchange at the token endpoint of a project.
public static class TokenExchange
{
    public const string GrantType = "urn:ietf:params:oauth:grant-type:token-exchange";
    public const string JwtTokenType = "urn:ietf:params:oauth:token-type:jwt";
    public const string IdTokenType = "urn:ietf:params:oauth:token-type:id_token";
    public const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";
}
