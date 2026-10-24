namespace _42.Platform.Storyteller.Mcp.Authentication;

// Gets an access token for the Storyteller API on behalf of the person whose MCP token is given.
public interface IStorytellerTokenProvider
{
    Task<string> GetTokenAsync(string userToken, CancellationToken cancellationToken);
}
