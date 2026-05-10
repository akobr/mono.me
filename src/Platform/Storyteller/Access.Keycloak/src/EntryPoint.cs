using _42.Platform.Storyteller.Accessing;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller;

public static class EntryPoint
{
    public static IServiceCollection AddKeycloakMachineAccess(
        this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddSingleton<IMachineAccessService, KeycloakMachineAccessService>();
        return services;
    }
}
