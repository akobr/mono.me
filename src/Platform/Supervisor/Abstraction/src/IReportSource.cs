namespace _42.Platform.Supervisor;

public interface IReportSource
{
    string Application { get; }

    string? Version { get; }

    string? CodeSource { get; }
}
