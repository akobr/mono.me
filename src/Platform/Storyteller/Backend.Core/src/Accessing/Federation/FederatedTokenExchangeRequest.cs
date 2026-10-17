namespace _42.Platform.Storyteller.Accessing.Federation;

// A token request whose form the endpoint already checked: parameters present, grant type, audience and resource,
// the syntax of scope and the requested token type.
public sealed record FederatedTokenExchangeRequest
{
    public required string Organization { get; init; }

    public required string Project { get; init; }

    // client_id: the id of the Federated machine.
    public required string MachineId { get; init; }

    // The platform token, still unverified.
    public required string SubjectToken { get; init; }

    public required string SubjectTokenType { get; init; }

    // The Storyteller scopes the scope parameter asks for. Null: every scope of the machine.
    public IReadOnlyList<string>? Scopes { get; init; }

    // Echoed as issued_token_type: TokenTypes.AccessToken or TokenTypes.Jwt.
    public string RequestedTokenType { get; init; } = OAuthTokenExchange.TokenTypes.AccessToken;
}
