using System.Text;

namespace _42.Platform.Storyteller.Mcp.Authentication;

// A Storyteller user token from the API's token exchange: the bearer token for the API calls of one person, when it
// expires, the granted scope and the restrictions of the MCP server's actor for the person's client app (NoSecretReveal
// unless the API's actor sets AllowSecrets, and ReadOnly with NoSecretReveal for a client app the API does not trust).
// Its text never shows the token.
public sealed record StorytellerToken(string AccessToken, DateTimeOffset ExpiresAt, string Scope, IReadOnlySet<string> Restrictions)
{
    // The API refuses secret reads to the actor (403 ActorRestricted); the server hides and refuses its secret tools.
    public const string NoSecretReveal = "NoSecretReveal";

    // The API lets the person's client app only read (403 ActorReadOnly for every change); the server hides and refuses
    // every tool outside the read-only tool set (ReadOnlyToolSet).
    public const string ReadOnly = "ReadOnly";

    public bool WithholdsSecrets => Restrictions.Contains(NoSecretReveal);

    public bool IsReadOnly => Restrictions.Contains(ReadOnly);

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"ExpiresAt = {ExpiresAt:O}, Scope = {Scope}, Restrictions = [{string.Join(", ", Restrictions)}]");
        return true;
    }
}
