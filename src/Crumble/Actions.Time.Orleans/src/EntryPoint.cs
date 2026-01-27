using Microsoft.Extensions.DependencyInjection;

namespace _42.Crumble;

public static class EntryPoint
{
    public static IServiceCollection AddTimeActions(this IServiceCollection @this)
    {
        @this.AddSingleton<IStartupTask, TimeActionsBootstrap>();
        return @this;
    }
}
