using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Binding;

public static class EntryPoint
{
    public static IServiceCollection AddConfigurationBindings(
        this IServiceCollection @this,
        Action<BindingsOptions>? configure = null)
    {
        @this.TryAddSingleton<BindingService>();
        @this.TryAddSingleton<IBindingRegistry>(provider => provider.GetRequiredService<BindingService>());

        @this.TryAddSingleton<IBindingExecutor>(provider =>
        {
            var service = provider.GetRequiredService<BindingService>();
            var options = provider.GetRequiredService<IOptions<BindingsOptions>>();

            foreach (var (key, strategy) in options.Value.Resolve(provider))
            {
                service.RegisterStrategy(key, strategy);
            }

            return service;
        });

        if (configure is not null)
        {
            @this.Configure(configure);
        }

        return @this;
    }
}
