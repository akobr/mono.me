using System.Security.Claims;

namespace _42.Platform.Storyteller.Accessing;

public sealed record BearerValidationResult(
    IReadOnlyList<Claim> Claims,
    bool IsMachine,
    string? MachineId);
