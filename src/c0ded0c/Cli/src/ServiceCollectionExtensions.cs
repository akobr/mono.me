using Microsoft.Extensions.DependencyInjection;

namespace c0ded0c.Cli
{
    public static class ServiceCollectionExtensions
    {
        public static void AddSingleton<TInterface1, TInterface2, TImplementation>(this IServiceCollection services)
            where TImplementation : class, TInterface1, TInterface2
            where TInterface1 : class
            where TInterface2 : class
        {
            services.AddSingleton<TImplementation>();
            services.AddSingleton<TInterface1>((services) => services.GetRequiredService<TImplementation>());
            services.AddSingleton<TInterface2>((services) => services.GetRequiredService<TImplementation>());
        }
    }
}
