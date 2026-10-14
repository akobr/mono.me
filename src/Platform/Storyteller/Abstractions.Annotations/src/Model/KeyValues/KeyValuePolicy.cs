using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller;

// What a project allows and uses for its key/value secrets. Stored on the project's access point.
public record class KeyValuePolicy
{
    public IReadOnlyList<KeyValueProtection> AllowedProtections { get; init; } = [KeyValueProtection.Public, KeyValueProtection.Standard];

    public KeyValueProtection DefaultSecretProtection { get; init; } = KeyValueProtection.Standard;

    // Machines receive secret values only when they authenticate with an mTLS certificate.
    public bool RequireMtlsForSecretDelivery { get; init; } = true;

    // Read-only, set by the platform.
    public ProtectionKeyInfo? Standard { get; init; }

    public HsmSettings? Hsm { get; init; }

    public ByokSettings? Byok { get; init; }

    public IReadOnlyList<ClientEncryptionRecipient> Recipients { get; init; } = [];

    // Read-only, the re-wrap in progress after a key rotation.
    public RewrapStatus? Rewrap { get; init; }

    public ulong Version { get; init; }

    // Null for the defaults.
    public AuditStamp? Modified { get; init; }
}

public record class ProtectionKeyInfo
{
    // The versionless key-encryption key id.
    public required string KeyId { get; init; }

    public required string CurrentKeyVersion { get; init; }

    public DateTimeOffset? NextRotationAt { get; init; }

    public DateTimeOffset? CheckedAt { get; init; }
}

[JsonConverter(typeof(StringEnumConverter))]
public enum HsmBackend
{
    KeyVaultPremium = 0,
    ManagedHsm,
}

public record class HsmSettings
{
    // Read-only: a shared Premium vault, or the organization's own Managed HSM.
    public HsmBackend Backend { get; init; }

    public ProtectionKeyInfo? Key { get; init; }

    public TimeSpan RotationInterval { get; init; } = TimeSpan.FromDays(365);
}

[JsonConverter(typeof(StringEnumConverter))]
public enum CustomerKeyHealth
{
    Unknown = 0,
    Healthy,
    Unavailable,
}

// The customer's own key-encryption key, in the customer's vault and tenant.
public record class ByokSettings
{
    // Versionless key id, e.g. https://customer.vault.azure.net/keys/storyteller-kek
    public required string KeyUri { get; init; }

    public required string TenantId { get; init; }

    public DateTimeOffset? VerifiedAt { get; init; }

    public string? CurrentKeyVersion { get; init; }

    public CustomerKeyHealth Health { get; init; }

    public DateTimeOffset? LastCheckedAt { get; init; }

    public string? LastError { get; init; }
}

// An RSA public key as a JWK (RFC 7517), the form clients encrypt for.
public record class RsaJsonWebKey
{
    public string Kty { get; init; } = "RSA";

    public required string N { get; init; }

    public required string E { get; init; }
}

// Who can read client-encrypted (Hyok) values: an application of the customer with its own key pair.
public record class ClientEncryptionRecipient
{
    // The JWE kid, [A-Za-z0-9_-]{1,64}.
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required RsaJsonWebKey PublicKey { get; init; }

    public bool IsEnabled { get; init; } = true;

    public required AuditStamp Created { get; init; }
}

public record class RewrapStatus
{
    public required KeyValueProtection Protection { get; init; }

    public required string TargetKeyVersion { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public long Processed { get; init; }

    public long Remaining { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string? LastError { get; init; }
}
