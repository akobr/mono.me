namespace _42.Platform.Storyteller.Mcp.Authentication;

// Gets a Storyteller token that acts for the person whose MCP token is given.
public interface IStorytellerTokenProvider
{
    Task<StorytellerToken> GetTokenAsync(string userToken, CancellationToken cancellationToken);

    // Drops the cached token of the person's MCP token, but only while it is still the refused accessToken: a fresh one
    // that another call already got stays.
    void Forget(string userToken, string accessToken);
}
