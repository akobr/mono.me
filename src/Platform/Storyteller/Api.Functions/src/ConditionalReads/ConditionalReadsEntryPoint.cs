using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Api.ConditionalReads;

public static class ConditionalReadsEntryPoint
{
    // An invalid ConditionalReads:EntityTagKey stops the start; the message names the setting, never its value.
    public static IServiceCollection AddConditionalReads(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ConditionalReadOptions>()
            .Bind(configuration.GetSection(ConditionalReadOptions.SectionName))
            .Validate(
                options => options.IsValid,
                $"{ConditionalReadOptions.SectionName}:{nameof(ConditionalReadOptions.EntityTagKey)} must be base64 of at least {ConditionalReadOptions.MinimumKeyBytes} bytes.")
            .ValidateOnStart();
        return services;
    }
}
