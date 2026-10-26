using System.Text;

namespace _42.Platform.Storyteller.Accessing.Federation;

// A token request of POST v1/access/token for a user token: the parameters as the form carries them, null or empty when
// absent. The endpoint checks the transport and the form (content type, size, repeated parameters); the exchange checks
// the values, so no other caller skips them.
public sealed record UserTokenExchangeRequest
{
    public string? GrantType { get; init; }

    // The actor id (Auth:TokenExchange:Actors:Id).
    public string? ClientId { get; init; }

    // The person's token, still unverified.
    public string? SubjectToken { get; init; }

    public string? SubjectTokenType { get; init; }

    // The actor's proof: exactly one of actor_token (WorkloadIdentity), client_assertion (ClientAssertion) and
    // client_secret (ClientSecretHash, Development only).
    public string? ActorToken { get; init; }

    public string? ActorTokenType { get; init; }

    public string? ClientAssertion { get; init; }

    public string? ClientAssertionType { get; init; }

    public string? ClientSecret { get; init; }

    // Every audience and resource must be the API (MachineAuth:Federation:Audience).
    public IReadOnlyList<string> Audiences { get; init; } = [];

    public IReadOnlyList<string> Resources { get; init; } = [];

    // Space separated. Null: every scope of the actor.
    public string? Scope { get; init; }

    // Null: TokenTypes.AccessToken, the only one issued.
    public string? RequestedTokenType { get; init; }

    // The URL of this token endpoint built from configuration (MachineAuth:Federation:PublicBaseUrl), never from the
    // request's Host header: a client assertion may name it as its aud, next to the Storyteller issuer. Null: the issuer only.
    public string? TokenEndpoint { get; init; }

    // Tokens and the secret stay out of ToString, so a logged request never carries a credential.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"GrantType = {GrantType}, ClientId = {ClientId}, SubjectTokenType = {SubjectTokenType}, ");
        builder.Append($"ActorTokenType = {ActorTokenType}, ClientAssertionType = {ClientAssertionType}, ");
        builder.Append($"Audiences = [{string.Join(", ", Audiences)}], Resources = [{string.Join(", ", Resources)}], ");
        builder.Append($"Scope = {Scope}, RequestedTokenType = {RequestedTokenType}, TokenEndpoint = {TokenEndpoint}");
        return true;
    }
}
