using System.Security.Claims;

namespace _42.Platform.Storyteller.Accessing;

// CredentialKind, Organization and Project are set by validators whose tokens name them (Storyteller tokens of
// Federated machines); null keeps the kind ClientCredentials and leaves the project to the route.
public sealed record BearerValidationResult(
    IReadOnlyList<Claim> Claims,
    bool IsMachine,
    string? MachineId,
    MachineCredentialKind? CredentialKind = null,
    string? Organization = null,
    string? Project = null);
