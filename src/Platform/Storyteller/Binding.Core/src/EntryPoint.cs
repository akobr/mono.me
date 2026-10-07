using _42.Platform.Storyteller.Binding.Language;
using _42.Platform.Storyteller.Binding.Object;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Binding;

public static class EntryPoint
{
    public static IServiceCollection AddConfigurationBindings(
        this IServiceCollection @this,
        Action<BindingsOptions>? configure = null)
    {
        @this.TryAddSingleton<BindingExecutor>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<BindingsOptions>>();
            var executor = new BindingExecutor();

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
        @this.TryAddSingleton<IBindingRegistry>(provider => provider.GetRequiredService<BindingExecutor>());
        @this.TryAddSingleton<IBindingExecutor>(provider => provider.GetRequiredService<BindingExecutor>());
        @this.TryAddSingleton<IConfigurationBindingResolver>(provider =>
            new ConfigurationBindingResolver(
                provider.GetRequiredService<BindingExecutor>(),
                provider.GetService<ILogger<ConfigurationBindingResolver>>()));

        if (configure is not null)
        {
            @this.Configure(configure);
        }

        return @this;
    }
}
