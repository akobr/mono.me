using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public interface IAccessPoint
{
    string Key { get; }

    IReadOnlyDictionary<string, AccountRole> AccessMap { get; }

    MachineAuthenticationPolicy? MachineAuthentication { get; }

    // Only a project has a description; always null for an organization.
    string? Description { get; }
}
