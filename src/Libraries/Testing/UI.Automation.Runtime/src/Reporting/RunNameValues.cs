namespace _42.Testing.UI.Automation.Runtime.Reporting;

// What the placeholders of Reporting:RunName and Reporting:RunDescription stand for. StartedAt is local time, so
// {Date} and {Time} read as on the machine that ran.
public sealed record RunNameValues
{
    public required string RunId { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public string? Profile { get; init; }

    public string? Provider { get; init; }

    public string? Engine { get; init; }

    public string? Device { get; init; }

    public string? Branch { get; init; }

    public string? Sha { get; init; }

    public string? BuildNumber { get; init; }

    public string? Machine { get; init; }
}
