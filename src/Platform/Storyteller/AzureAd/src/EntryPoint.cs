using _42.Platform.Storyteller.Backend.Accessing;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.AzureAd;

public static class EntryPoint
{
    public static IServiceCollection AddAzureAdMachineAccess(
        this IServiceCollection services)
    {
        services.AddSingleton<IMachineAccessService, AzureAdMachineAccessService>();
        return services;
    }
}
