using _42.Platform.Storyteller.Sdk.Configuring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// The namespace of IHostApplicationBuilder, so builder.AddStoryteller(…) needs no using.
namespace Microsoft.Extensions.Hosting;

public static class StorytellerHostApplicationBuilderExtensions
{
    // One call for a process with a key: builder.Configuration.AddStoryteller(configure), then
    // builder.Services.AddStoryteller(configure). The provider, its refresher and logs, who am I, the catalog and the
    // documents, from one delegate, which runs once for each. The provider requires a key (STORYTELLER_KEY or
    // options.Key), so this call does too; the variables come from the environment, or from an Aspire AppHost.
    public static IStorytellerHostingBuilder AddStoryteller(this IHostApplicationBuilder builder, Action<StorytellerConfigurationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Configuration.AddStoryteller(configure);
        return builder.Services.AddStoryteller(configure);
    }
}
