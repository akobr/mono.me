namespace _42.Crumble;

public record ActionModel<TAction, TInput>
    where TAction : ActionAttribute
{
    public required string CrumbKey { get; init; }

    public TAction Action { get; init; }

    public Func<IServiceProvider, TInput, Task> Executor { get; init; }
}
