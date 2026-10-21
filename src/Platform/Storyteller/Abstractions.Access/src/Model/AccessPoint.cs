using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public record class AccessPoint : IAccessPoint
{
    public required string Key { get; init; }

    public required IReadOnlyDictionary<string, AccountRole> AccessMap { get; init; }

    public MachineAuthenticationPolicy? MachineAuthentication { get; init; }

    // Only a project has a description; always null for an organization.
    public string? Description { get; init; }
}
