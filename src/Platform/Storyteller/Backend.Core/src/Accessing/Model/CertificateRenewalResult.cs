using System;

namespace _42.Platform.Storyteller.Accessing.Model;

public record CertificateRenewalResult
{
    public required CertificateRenewalOutcome Outcome { get; init; }

    public byte[]? Pkcs12 { get; init; }

    public string? Password { get; init; }

    public string? Thumbprint { get; init; }

    public DateTimeOffset? LastRenewalAt { get; init; }

    public string? Message { get; init; }
}
