using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace _42.Platform.Supervisor;

public record class Report : IReport
{
    public required ReportType Type { get; init; }

    public required IReportSource Source { get; init; }

    public string? Text { get; set; }

    public JsonNode? Data { get; set; }

    public IDictionary<string, string>? Values { get; set; }

    public ISet<string>? Flags { get; set; }

    public string? StackTrace { get; set; }
}
