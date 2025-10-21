namespace _42.Crumble;

public class CrumbInnerExecutionContext : ICrumbInnerExecutionContext
{
    public required string CrumbKey { get; set; }

    public object? Instance { get; set; }

    public object? Input { get; set; }

    public object? Output { get; set; }

    public ICrumbExecutionContext ExecutionContext { get; set; }

    public required ICrumbExecutionSetting Settings { get; set; }
}
