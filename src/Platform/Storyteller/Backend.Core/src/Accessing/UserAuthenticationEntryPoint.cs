using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Accessing;

public static class UserAuthenticationEntryPoint
{
    private const string PermissionMapPath = "AuthKit:PermissionMap";

    // Shared by every user and machine identity provider registration. Idempotent: binding the
    // section twice would append array values such as DefaultUserScopes a second time.
    public static IServiceCollection AddUserAuthenticationOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(UserAuthenticationOptionsMarker)))
        {
            return services;
        }

        var section = configuration.GetSection(UserAuthenticationOptions.SectionName);

        services.AddSingleton<UserAuthenticationOptionsMarker>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<UserAuthenticationOptions>, UserAuthenticationOptionsValidator>());
        services.AddOptions<UserAuthenticationOptions>()
            .Bind(section)
            .Configure(options => options.AuthKit.PermissionMap = ReadPermissionMap(section.GetSection(PermissionMapPath)))
            .ValidateOnStart();
        return services;
    }

    // ':' is the configuration path separator, so the binder splits a slug such as
    // "storyteller:annotation-read" into nested sections. Every leaf below the map is
    // read here with its relative path as the WorkOS permission slug.
    public static Dictionary<string, string> ReadPermissionMap(IConfigurationSection section)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (slug, scopes) in section.AsEnumerable(makePathsRelative: true))
        {
            if (!string.IsNullOrWhiteSpace(scopes))
            {
                map[slug] = scopes;
            }
        }

        return map;
    }

    // Single identity provider for machine client credentials; every Add…MachineAccess calls this.
    public static void EnsureNoOtherIdentityProviderMachineAccess(this IServiceCollection services, string provider)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IIdentityProviderMachineAccessService)))
        {
            throw new InvalidOperationException(
                $"An identity provider for ClientCredentials machine access is already registered; {provider} cannot be added as well.");
        }
    }

    private sealed class UserAuthenticationOptionsMarker
    {
    }
}
