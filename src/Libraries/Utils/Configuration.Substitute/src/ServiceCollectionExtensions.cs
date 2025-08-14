using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Utils.Configuration.Substitute;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddConfigurationSubstitutions(this IServiceCollection @this, Action<ISubstitutionRegistry, IServiceProvider> configure)
    {
        @this.AddSingleton<SubstitutionService>(p =>
        {
            var service = new SubstitutionService();
            configure(service, p);
            return service;
        });

        @this.AddSingleton<ISubstitutionExecutor>(p => p.GetRequiredService<SubstitutionService>());
        @this.AddSingleton<ISubstitutableConfiguration>(p =>
        {
            var configuration = p.GetRequiredService<IConfiguration>();

            if (configuration is ISubstitutableConfiguration substitutable)
            {
                return substitutable;
            }

            return new SubstitutableConfiguration(configuration, p.GetRequiredService<ISubstitutionExecutor>());
        });

        return @this;
    }

    public static IServiceCollection MakeConfigurationSubstitutable(this IServiceCollection @this, IConfiguration wrappedConfiguration)
    {
        return @this.AddSingleton<IConfiguration>(
            p =>
            {
                var executor = p.GetRequiredService<ISubstitutionExecutor>();
                return new SubstitutableConfiguration(wrappedConfiguration, executor);
            });
    }
}
