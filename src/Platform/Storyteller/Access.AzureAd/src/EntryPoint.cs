using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

public static class EntryPoint
{
    public static IServiceCollection AddAzureAdMachineAccess(
        this IServiceCollection services)
    {
        services.AddSingleton<IMachineAccessService, AzureAdMachineAccessService>();
        return services;
    }

    public static IServiceCollection AddEntraIdUserAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<UserAuthenticationOptions>, UserAuthenticationOptionsValidator>();
        services.AddOptions<UserAuthenticationOptions>()
            .Bind(configuration.GetSection(UserAuthenticationOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IBearerTokenValidator, EntraIdBearerTokenValidator>();
        return services;
    }
}
