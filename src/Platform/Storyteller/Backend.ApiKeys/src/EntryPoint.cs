using _42.Platform.Storyteller.Accessing;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller;

public static class EntryPoint
{
    public static IServiceCollection AddApiKeyMachineAccess(
        this IServiceCollection services)
    {
        services.AddSingleton<IMachineAccessService, ApiKeyMachineAccessService>();
        return services;
    }
}
