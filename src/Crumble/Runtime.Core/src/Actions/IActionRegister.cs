namespace _42.Crumble;

public interface IActionRegister
{
    IActionRegister RegisterAction<TAction, TInput>(ActionModel<TAction, TInput> model)
        where TAction : ActionAttribute;
}
