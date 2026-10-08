using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Accessing;

public static class UserAuthenticationEntryPoint
{
    private const string PermissionMapPath = "AuthKit:PermissionMap";

    // Shared by every user identity provider registration.
    public static IServiceCollection AddUserAuthenticationOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(UserAuthenticationOptions.SectionName);

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
}
