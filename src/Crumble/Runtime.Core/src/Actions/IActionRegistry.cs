namespace _42.Crumble;

public interface IActionRegistry
{
    void RegisterAction(ActionAttribute model, Action executor);

    IEnumerable<(TAction Model, Action Executor)> GetActions<TAction>()
    where TAction : ActionAttribute;
}
