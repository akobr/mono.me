using System;
using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public interface IMachineAccess
{
    string Id { get; }

    string ObjectId { get; }

    string AccessKey { get; }

    IReadOnlyList<MachineScope> Scopes { get; }

    string? AnnotationKey { get; }

    MachineCredentialKind CredentialKind { get; }

    string? CertificateThumbprint { get; }

    string? Certificate { get; }

    string? CertificatePassword { get; }

    DateTimeOffset? LastRenewalAt { get; }

    int? CertificateLifetimeDays { get; }

    DateTimeOffset? CertificateNotAfter { get; }

    bool UsesSharedCertificate { get; }

    string? TokenEndpoint { get; }

    string? TokenScope { get; }

    IReadOnlyList<FederationRule>? FederationRules { get; }
}
