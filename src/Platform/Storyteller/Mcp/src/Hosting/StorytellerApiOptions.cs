namespace _42.Platform.Storyteller.Mcp.Hosting;

// Section Storyteller: the API the tools call, and the delegated scopes the server asks for on behalf of the person.
public sealed class StorytellerApiOptions
{
    public const string SectionName = "Storyteller";

    // The API base URL with /api, for example https://api.example.com/api.
    public string BaseUrl { get; set; } = string.Empty;

    // For example api://{ApiClientId}/User.Impersonation.
    public List<string> Scopes { get; set; } = [];
}
