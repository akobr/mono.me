using _42.Platform.Storyteller.Binding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Api.ConditionalReads;

public static class ConditionalReadsEntryPoint
{
    // An invalid ConditionalReads setting stops the start; the message names the setting, never its value.
    public static IServiceCollection AddConditionalReads(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ConditionalReadOptions>()
            .Bind(configuration.GetSection(ConditionalReadOptions.SectionName))
            .Validate(
                options => options.IsValid,
                $"{ConditionalReadOptions.SectionName}:{nameof(ConditionalReadOptions.EntityTagKey)} must be base64 of at least {ConditionalReadOptions.MinimumKeyBytes} bytes.")
            .Validate(
                options => options.IsManifestCacheValid,
                $"{ConditionalReadOptions.SectionName}:{nameof(ConditionalReadOptions.ManifestLifetime)} and {ConditionalReadOptions.SectionName}:{nameof(ConditionalReadOptions.ManifestCacheSizeLimit)} must be positive.")
            .ValidateOnStart();
        services.AddSingleton(provider => new DependencyManifests(
            provider.GetRequiredService<IOptions<ConditionalReadOptions>>(),
            provider.GetService<IBindingCatalog>()));
        return services;
    }
}
