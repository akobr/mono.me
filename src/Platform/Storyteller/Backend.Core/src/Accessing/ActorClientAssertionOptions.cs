namespace _42.Platform.Storyteller.Accessing;

// The keys an actor signs its RFC 7523 client assertions with.
public class ActorClientAssertionOptions
{
    // The actor's published key set, for example https://mcp.example.com/.well-known/jwks.json.
    public string JwksUri { get; set; } = string.Empty;
}
