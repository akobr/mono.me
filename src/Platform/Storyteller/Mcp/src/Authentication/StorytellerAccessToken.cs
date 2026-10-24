namespace _42.Platform.Storyteller.Mcp.Authentication;

// The Storyteller token of the current request, set by the call filter and read by the SDK clients of the same scope.
public sealed class StorytellerAccessToken
{
    public string? Value { get; set; }
}
