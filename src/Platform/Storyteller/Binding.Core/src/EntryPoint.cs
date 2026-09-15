using _42.Platform.Storyteller.Binding.Language;
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
        @this.TryAddSingleton<BindingExecutor>();
        @this.TryAddSingleton<IBindingRegistry>(provider => provider.GetRequiredService<BindingExecutor>());

        @this.TryAddSingleton<IBindingExecutor>(provider =>
        {
            var executor = provider.GetRequiredService<BindingExecutor>();
            var options = provider.GetRequiredService<IOptions<BindingsOptions>>();

            foreach (var (key, source) in options.Value.ResolveSources(provider))
            {
                executor.RegisterSource(key, source);
            }

            foreach (var (name, function) in options.Value.ResolveFunctions(provider))
            {
                executor.RegisterFunction(name, function);
            }

            return executor;
        });

        if (configure is not null)
        {
            @this.Configure(configure);
        }

        return @this;
    }
}
