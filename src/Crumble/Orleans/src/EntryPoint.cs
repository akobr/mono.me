using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace _42.Crumble;

public static class EntryPoint
{
    public static IServiceCollection AddCrumble(this IServiceCollection @this)
    {
        // crumbs registry
        @this.TryAddSingleton<CrumbToGrainRegistry>();
        @this.TryAddSingleton<ICrumbToGrainRegister>(
            services => services.GetRequiredService<CrumbToGrainRegistry>());
        @this.TryAddSingleton<ICrumbToGrainProvider>(
            services => services.GetRequiredService<CrumbToGrainRegistry>());

        // crumbs execution
        @this.TryAddSingleton<ICrumbExecutorFactory, CrumbExecutorFactory>();
        @this.TryAddScoped<ICrumbExecutor>(
            services => services.GetRequiredService<ICrumbExecutorFactory>().CreateExecutor());

        // crumbs context
        @this.TryAddSingleton<ICrumbExecutionContextProvider, OrleansCrumbExecutionContextProvider>();

        // inner logging (tracing of crumb execution)
        @this.TryAddSingleton<ICrumbLoggerFactory, CrumbLoggerFactory>();
        @this.TryAddScoped<ICrumbLogger>(
            services => services.GetRequiredService<ICrumbLoggerFactory>().CreateLogger());

        // middlewares
        @this.TryAddSingleton<IMiddlewaresProvider, BlankMiddlewaresProvider>();

        return @this;
    }
}
