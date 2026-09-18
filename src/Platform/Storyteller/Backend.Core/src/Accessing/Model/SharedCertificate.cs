using System;

namespace _42.Platform.Storyteller.Accessing.Model;

public record SharedCertificate
{
    public required string Thumbprint { get; init; }

    public required string Label { get; init; }

    public required DateTimeOffset NotBefore { get; init; }

    public required DateTimeOffset NotAfter { get; init; }

    public bool IsRevoked { get; init; }

    public string? Certificate { get; init; }

    public string? CertificatePassword { get; init; }
}
