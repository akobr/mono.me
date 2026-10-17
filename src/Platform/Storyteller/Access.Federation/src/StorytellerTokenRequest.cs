namespace _42.Platform.Storyteller;

// What a Storyteller token says about a Federated machine after an exchange.
public sealed record StorytellerTokenRequest
{
    public required string Organization { get; init; }

    public required string Project { get; init; }

    public required string MachineId { get; init; }

    // The granted Storyteller scopes.
    public required IReadOnlyList<string> Scopes { get; init; }

    public string? AnnotationKey { get; init; }

    // The trusted issuer name and the platform `sub` of the exchanged token.
    public required string FederationIssuer { get; init; }

    public required string FederationSubject { get; init; }
}
