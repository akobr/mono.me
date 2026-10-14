using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public record MachineAuthenticationPolicy
{
    // The kinds the machines of the project may use. Null or empty: only CredentialKind,
    // as in policies stored before a project could enable several kinds.
    public IReadOnlyList<MachineCredentialKind>? EnabledCredentialKinds { get; init; }

    // The default kind: preselected for a new machine and used when a create request names none.
    public MachineCredentialKind? CredentialKind { get; init; }

    // The default certificate lifetime of new machines. Null: MachineAuth:CertificateLifetimeDays.
    public int? CertificateLifetimeDays { get; init; }
}
