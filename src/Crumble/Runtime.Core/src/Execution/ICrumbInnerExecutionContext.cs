namespace _42.Crumble;

public interface ICrumbInnerExecutionContext
{
    string CrumbKey { get; }

    object? Instance { get; }

    object? Input { get; }

    object? Output { get; }

    DateTimeOffset StartTime { get; }

    Exception? Exception { get; set; }

    ICrumbExecutionContext ExecutionContext { get; }

    ICrumbExecutionSetting Settings { get; }
}
