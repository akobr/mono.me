namespace _42.Platform.Supervisor;

public record class ReportSource : IReportSource
{
    public required string Application { get; set; }

    public string? Version { get; set; }

    public string? CodeSource { get; set; }
}
