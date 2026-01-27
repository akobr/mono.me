namespace _42.Crumble;

public interface IActionRegistry : IActionRegister
{
    IEnumerable<ActionModel<TAction, TInput>> GetActions<TAction, TInput>()
        where TAction : ActionAttribute;
}
