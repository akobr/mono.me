using System.Text;

namespace _42.Platform.Storyteller.Mcp.Authentication;

// A Storyteller user token from the API's token exchange: the bearer token for the API calls of one person, when it
// expires, the granted scope and the restrictions of the MCP server's actor (NoSecretReveal unless the API's actor sets
// AllowSecrets). Its text never shows the token.
public sealed record StorytellerToken(string AccessToken, DateTimeOffset ExpiresAt, string Scope, IReadOnlySet<string> Restrictions)
{
    // The API refuses secret reads to the actor (403 ActorRestricted); the server hides and refuses its secret tools.
    public const string NoSecretReveal = "NoSecretReveal";

    public bool WithholdsSecrets => Restrictions.Contains(NoSecretReveal);

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"ExpiresAt = {ExpiresAt:O}, Scope = {Scope}, Restrictions = [{string.Join(", ", Restrictions)}]");
        return true;
    }
}
