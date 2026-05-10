using _42.Platform.Storyteller.Accessing;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller;

public static class EntryPoint
{
    public static IServiceCollection AddApiKeyMachineAccess(
        this IServiceCollection services)
    {
        services.AddSingleton<ApiKeyMachineAccessService>();
        services.AddSingleton<IMachineAccessService>(p => p.GetRequiredService<ApiKeyMachineAccessService>());
        services.AddSingleton<IApiKeyValidator>(p => p.GetRequiredService<ApiKeyMachineAccessService>());
        return services;
    }
}
