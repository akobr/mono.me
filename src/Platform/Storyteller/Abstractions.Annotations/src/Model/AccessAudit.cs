using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller;

// What changed in the access to an organization or a project. New values go to the end: the stored records keep their names.
[JsonConverter(typeof(StringEnumConverter))]
public enum AccessAuditAction
{
    MemberGranted = 0,
    MemberRevoked,
    MemberRoleChanged,
    MemberRemoved,
    MemberJoined,
    AccessPointCreated,
    AccessPointUpdated,
    MachineCreated,
    MachineReset,
    MachineDeleted,
    MachineFederationRulesChanged,
    MachineAuthenticationPolicyChanged,
    KeyValuePolicyChanged,
    FederatedIssuerCreated,
    FederatedIssuerUpdated,
    FederatedIssuerDeleted,
    SharedCertificateIssued,
    SharedCertificateRevoked,
}

[JsonConverter(typeof(StringEnumConverter))]
public enum AccessAuditTargetKind
{
    Account = 0,
    Machine,
    Issuer,
    Certificate,
}

// What a change was made to: an account, a machine, a trusted issuer or a shared certificate. Name is a display snapshot
// taken at the change, so a removed member keeps one; it is never looked up and may be stale.
public record class AccessAuditTarget
{
    public required AccessAuditTargetKind Kind { get; init; }

    public required string Id { get; init; }

    public string? Name { get; init; }

    public static AccessAuditTarget Account(string id, string? name = null) => new() { Kind = AccessAuditTargetKind.Account, Id = id, Name = name };

    public static AccessAuditTarget Machine(string id) => new() { Kind = AccessAuditTargetKind.Machine, Id = id };

    public static AccessAuditTarget Issuer(string name) => new() { Kind = AccessAuditTargetKind.Issuer, Id = name };

    public static AccessAuditTarget Certificate(string thumbprint, string? label = null) => new() { Kind = AccessAuditTargetKind.Certificate, Id = thumbprint, Name = label };
}

// One record of an organization's access audit trail, written in the same transaction as the change it records and
// never changed afterwards. Never a secret: no key, client secret, certificate, password or hash.
public record class AccessAuditEntry
{
    public required string Id { get; init; }

    public required DateTimeOffset Time { get; init; }

    public required AccessAuditAction Action { get; init; }

    // Who made the change, with Via the actor that acted for the account (for example "Ada via mcp").
    public required AuditActor Actor { get; init; }

    // The organization, or the project as organization.project, whose access changed.
    public required string AccessPointKey { get; init; }

    public AccessAuditTarget? Target { get; init; }

    // A role name, a description or compact JSON; null when there was nothing before, or nothing is left.
    public string? OldValue { get; init; }

    public string? NewValue { get; init; }

    // What the values do not say, for example the invitation of a join.
    public string? Detail { get; init; }

    // The invocation of the API that made the change, to match its logs.
    public string? CorrelationId { get; init; }
}

public record class AccessAuditQuery
{
    public const int MaxPageSize = 100;

    // Only the records of this project; the organization's own records (its members) are left out.
    public string? Project { get; init; }

    public AccessAuditAction? Action { get; init; }

    // The id of the target: an account, a machine, an issuer name or a certificate thumbprint.
    public string? Target { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public string? ContinuationToken { get; init; }

    public int PageSize { get; init; } = MaxPageSize;
}

public record class AccessAuditResponse
{
    public required IReadOnlyList<AccessAuditEntry> Items { get; init; }

    public string? ContinuationToken { get; init; }

    public int Count { get; init; }
}
