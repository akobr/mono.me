namespace _42.Platform.Storyteller.Mcp.Hosting;

// Section Storyteller: the API the tools call, whose token exchange (POST {BaseUrl}/v1/access/token) gives the server a
// Storyteller token for the person.
public sealed class StorytellerApiOptions
{
    public const string SectionName = "Storyteller";

    // The API base URL with /api, for example https://api.example.com/api.
    public string BaseUrl { get; set; } = string.Empty;
}
