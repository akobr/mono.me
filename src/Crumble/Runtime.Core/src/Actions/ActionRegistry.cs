using Microsoft.Extensions.Options;

namespace _42.Crumble;

public class ActionRegistry(IOptions<ActionRegistryOptions> options) : IActionRegistry
{
    private readonly ActionRegistryOptions _registry = options.Value;

    public IActionRegister RegisterAction<TAction, TInput>(ActionModel<TAction, TInput> model)
        where TAction : ActionAttribute
    {
        _registry.RegisterAction(model);
        return this;
    }

    public IEnumerable<ActionModel<TAction, TInput>> GetActions<TAction, TInput>()
        where TAction : ActionAttribute
    {
        if (!_registry.Actions.TryGetValue(typeof(TAction), out var byActionType)
            || !byActionType.TryGetValue(typeof(TInput), out var byInputType))
        {
            return [];
        }

        return byInputType.Cast<ActionModel<TAction, TInput>>();
    }
}
