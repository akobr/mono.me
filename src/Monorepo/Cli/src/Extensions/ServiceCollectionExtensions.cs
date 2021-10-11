using System;
using _42.Monorepo.Cli.Cache;
using _42.Monorepo.Cli.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Monorepo.Cli.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static void AddOpStrategies(this IServiceCollection services, Action<IOpStrategiesRegister> configure)
        {
            services.AddSingleton<IGenericOpsCache, GenericOpsCache>();
            services.AddSingleton<IOpsExecutor, OpsExecutor>();

            services.AddSingleton<OpStrategiesFactory>();
            services.AddSingleton<IOpStrategiesRegister>(sp => sp.GetRequiredService<OpStrategiesFactory>());
            services.AddSingleton<IOpStrategiesFactory>(sp =>
            {
                var factory = sp.GetRequiredService<OpStrategiesFactory>();
                configure(factory);
                return factory;
            });
        }
    }
}
