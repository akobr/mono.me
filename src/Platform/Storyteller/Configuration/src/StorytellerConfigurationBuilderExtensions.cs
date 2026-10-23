using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Configuring.Engine;
using _42.Platform.Storyteller.Sdk.Configuring.Settings;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

// The namespace of every Microsoft and Azure configuration extension, so builder.Configuration.AddStoryteller(…) needs
// no using.
namespace Microsoft.Extensions.Configuration;

public static class StorytellerConfigurationBuilderExtensions
{
    // Adds the Storyteller configuration of STORYTELLER_KEY, or of options.Key. The options are read from the
    // environment and validated here, and throw StorytellerConfigurationException; the platform is read at the first
    // Load(), that is, when the builder builds.
    public static IConfigurationBuilder AddStoryteller(this IConfigurationBuilder builder, Action<StorytellerConfigurationOptions>? configure = null)
    {
        return builder.AddStoryteller(configure, out _);
    }

    // The same, with the refresher of the source: one per source, stable across rebuilds.
    public static IConfigurationBuilder AddStoryteller(
        this IConfigurationBuilder builder,
        Action<StorytellerConfigurationOptions>? configure,
        out IStorytellerConfigurationRefresher refresher)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new StorytellerConfigurationOptions();
        configure?.Invoke(options);

        var engine = new StorytellerConfigurationEngine(SettingsReader.Read(options, requireKey: true));
        var source = new StorytellerConfigurationSource(engine);

        // On a ConfigurationManager, Insert builds and loads every source again; Add builds and loads the new one only.
        if (engine.Settings.Placement == SourcePlacement.BeforeEnvironmentVariables && FindEnvironmentVariables(builder.Sources) is var index and >= 0)
        {
            builder.Sources.Insert(index, source);
        }
        else
        {
            builder.Add(source);
        }

        refresher = engine;
        return builder;
    }

    // The last unprefixed environment source: appsettings*.json < Storyteller < environment variables < command line,
    // as Host.CreateApplicationBuilder and WebApplication.CreateBuilder order them. -1 without one.
    private static int FindEnvironmentVariables(IList<IConfigurationSource> sources)
    {
        for (var index = sources.Count - 1; index >= 0; index--)
        {
            if (sources[index] is EnvironmentVariablesConfigurationSource { Prefix: null or "" })
            {
                return index;
            }
        }

        return -1;
    }
}
