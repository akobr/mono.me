using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Api.Exhibitions;

public static class ExhibitionEntryPoint
{
    private const string Organization = $"{ExhibitionOptions.SectionName}:{nameof(ExhibitionOptions.Organization)}";
    private const string Project = $"{ExhibitionOptions.SectionName}:{nameof(ExhibitionOptions.Project)}";
    private const string Owner = $"{ExhibitionOptions.SectionName}:{nameof(ExhibitionOptions.Owner)}";

    // One or two of the three Exhibition settings, a name that is no valid name, or an owner with white space around it,
    // stops the start; the message names the settings, never their values.
    public static IServiceCollection AddExhibition(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ExhibitionOptions>()
            .Bind(configuration.GetSection(ExhibitionOptions.SectionName))
            .Validate(
                options => options.IsValid,
                $"{Organization}, {Project} and {Owner} must be set together or not at all; {Organization} and {Project} must be valid names: 2 to 63 lower-case letters, digits or hyphens, starting with a letter or a digit; {Owner} must have no surrounding white space.")
            .ValidateOnStart();
        return services;
    }
}
