using _42.Platform.Storyteller.Accessing;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller;

public static class EntryPoint
{
    public static IServiceCollection AddAzureAdMachineAccess(
        this IServiceCollection services)
    {
        services.AddSingleton<IMachineAccessService, AzureAdMachineAccessService>();
        return services;
    }
}
