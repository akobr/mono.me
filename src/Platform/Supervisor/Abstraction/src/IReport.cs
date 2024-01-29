using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace _42.Platform.Supervisor;

public interface IReport
{
    ReportType Type { get; }

    IReportSource Source { get; }

    string? Text { get; }

    JsonNode? Data { get; }

    IDictionary<string, string>? Values { get; }

    ISet<string>? Flags { get; }

    string? StackTrace { get; }
}
