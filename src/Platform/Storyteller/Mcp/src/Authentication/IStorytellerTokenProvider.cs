namespace _42.Platform.Storyteller.Mcp.Authentication;

// Gets a Storyteller token that acts for the person whose MCP token is given.
public interface IStorytellerTokenProvider
{
    Task<StorytellerToken> GetTokenAsync(string userToken, CancellationToken cancellationToken);
}
