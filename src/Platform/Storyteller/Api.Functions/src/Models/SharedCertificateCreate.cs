namespace _42.Platform.Storyteller.Api.Models;

public record SharedCertificateCreate
{
    public required string Label { get; init; }

    public int? LifetimeDays { get; init; }
}
