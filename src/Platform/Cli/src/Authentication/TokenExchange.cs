namespace _42.Platform.Cli.Authentication;

// The values of an RFC 8693 token exchange at the token endpoint of a project, as sform prints them for a workload.
public static class TokenExchange
{
    public const string GrantType = "urn:ietf:params:oauth:grant-type:token-exchange";
    public const string JwtTokenType = "urn:ietf:params:oauth:token-type:jwt";
}
