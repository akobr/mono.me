using Microsoft.Extensions.DependencyInjection;

namespace _42.tHolistic.Runner.VisualStudio;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSingletonFromOtherService<TService, TSourceService>(
        this IServiceCollection @this)
    where TService : class
    where TSourceService : TService
    {
        return @this.AddSingleton<TService>(sp => sp.GetRequiredService<TSourceService>());
    }
}
