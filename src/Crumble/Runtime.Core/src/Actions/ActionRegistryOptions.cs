namespace _42.Crumble;

public class ActionRegistryOptions : IActionRegister
{
    public IActionRegister RegisterAction<TAction, TInput>(ActionModel<TAction, TInput> model)
        where TAction : ActionAttribute
    {
        var actionType = typeof(TAction);
        var inputType = typeof(TInput);

        Actions[actionType] ??= new();
        Actions[actionType][inputType] ??= [];
        Actions[actionType][inputType].Add(model);

        return this;
    }

    internal Dictionary<Type, Dictionary<Type, List<object>>> Actions { get; } = new();
}
