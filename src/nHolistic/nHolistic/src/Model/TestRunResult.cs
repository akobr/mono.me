namespace _42.nHolistic;

public class TestRunResult
{
    public required TestCase TestCase { get; init; }

    public TestRunOutcome Outcome { get; init; }

    public string? ErrorMessage { get; init; }

    public string? ErrorStackTrace { get; init; }

    public string? DisplayName { get; init; }

    public string? ComputerName { get; init; }

    public TimeSpan Duration { get; init; }

    public DateTimeOffset StartTime { get; init; }

    public DateTimeOffset EndTime { get; init; }

    public List<string> Attachments { get; } = new();

    public List<TestRunResultMessage> Messages { get; } = new();

    public List<TestCaseProperty> Properties { get; } = new();
}
