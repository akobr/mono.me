using _42.CLI.Toolkit.Output;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace _42.CLI.Toolkit;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCliToolkit(this IServiceCollection @this)
    {
        @this.TryAddSingleton<IExtendedConsole, ExtendedConsole>();
        @this.TryAddSingleton<IRenderer>(provider => provider.GetRequiredService<IExtendedConsole>());
        @this.TryAddSingleton<IPrompter>(provider => provider.GetRequiredService<IExtendedConsole>());
        @this.TryAddSingleton<IProgressReporter>(provider => provider.GetRequiredService<IExtendedConsole>());
        return @this;
    }
}
