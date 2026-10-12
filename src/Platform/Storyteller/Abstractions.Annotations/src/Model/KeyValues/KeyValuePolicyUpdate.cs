using System;
using System.Collections.Generic;

namespace _42.Platform.Storyteller;

// The writable part of a KeyValuePolicy; a missing member keeps its value.
public record class KeyValuePolicyUpdate
{
    public IReadOnlyList<KeyValueProtection>? AllowedProtections { get; init; }

    public KeyValueProtection? DefaultSecretProtection { get; init; }

    public bool? RequireMtlsForSecretDelivery { get; init; }

    public TimeSpan? HsmRotationInterval { get; init; }

    // The customer's key; an empty KeyUri removes the settings.
    public ByokSettingsUpdate? Byok { get; init; }

    public ulong? ExpectedVersion { get; init; }
}

public record class ByokSettingsUpdate
{
    public string? KeyUri { get; init; }

    public string? TenantId { get; init; }
}

// What a customer needs to set up BYOK: the app to consent to, and the role to grant.
public record class ByokSetupInfo
{
    public required string ClientId { get; init; }

    public required string ConsentUrl { get; init; }

    public required string RequiredRole { get; init; }
}

public record class ClientEncryptionRecipientCreate
{
    // The JWE kid; generated when missing.
    public string? Id { get; init; }

    public required string Name { get; init; }

    // A PEM public key or a JWK object.
    public required string PublicKey { get; init; }
}

public record class ClientEncryptionRecipientUpdate
{
    public string? Name { get; init; }

    public bool? IsEnabled { get; init; }
}

public record class KeyValueRotateRequest
{
    public required KeyValueProtection Protection { get; init; }
}
