using _42.Platform.Storyteller.Binding.Language;
using _42.Platform.Storyteller.Binding.Object;
using _42.Platform.Storyteller.KeyValues;
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
            var sources = options.Value.ResolveSources(provider).ToList();
            var functions = options.Value.ResolveFunctions(provider).ToList();

            // @name(path) is a source call when name is a source, so a name must not be both
            var clashes = sources.Select(source => source.Key)
                .Intersect(functions.Select(function => function.Key), StringComparer.Ordinal)
                .ToList();

            if (clashes.Count > 0)
            {
                throw new InvalidOperationException(
                    $"The binding name(s) {string.Join(", ", clashes.Select(name => $"'{name}'"))} are registered as both a source and a function.");
            }

            foreach (var (key, source) in sources)
            {
                executor.RegisterSource(key, source);
            }

            foreach (var (name, function) in functions)
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
                provider.GetService<ILogger<ConfigurationBindingResolver>>(),
                provider.GetRequiredService<IOptions<BindingsOptions>>().Value.ObjectBindingLimits,
                TimeProvider.System));

        if (configure is not null)
        {
            @this.Configure(configure);
        }

        return @this;
    }

    /// <summary>
    /// Registers the native key/value store as the <c>default</c>, <c>public</c> and <c>secret</c> binding sources.
    /// Call it after the other source registrations, so it owns those names.
    /// </summary>
    public static IServiceCollection AddKeyValueBindings(this IServiceCollection @this)
    {
        @this.Configure<BindingsOptions>(options =>
        {
            foreach (var (key, kind) in KeyValueBindingSources.Kinds)
            {
                options.AddSource(
                    provider => new KeyValueBindingSource(
                        new Lazy<IKeyValueService>(() => provider.GetRequiredService<IKeyValueService>()),
                        kind),
                    key);
            }
        });

        return @this;
    }
}
