namespace _42.Platform.Storyteller.Api.V1.Models;

// The user token exchange of the deployment (Auth:TokenExchange): whether actors such as the MCP server may exchange
// user tokens at v1/access/token. Which actors may is not published.
public record class TokenExchangeConfiguration
{
    public required bool Enabled { get; init; }
}
