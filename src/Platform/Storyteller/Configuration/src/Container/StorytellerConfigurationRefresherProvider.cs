using Microsoft.Extensions.Configuration;

namespace _42.Platform.Storyteller.Sdk.Configuring.Container;

internal sealed class StorytellerConfigurationRefresherProvider : IStorytellerConfigurationRefresherProvider
{
    private readonly IConfiguration _configuration;

    public StorytellerConfigurationRefresherProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IReadOnlyList<IStorytellerConfigurationRefresher> Refreshers => StorytellerEngines.Find(_configuration);

    // The refresher of IStorytellerConfigurationRefresher: the only one.
    public static IStorytellerConfigurationRefresher GetSingle(IStorytellerConfigurationRefresherProvider provider)
    {
        return provider.Refreshers switch
        {
            [var single] => single,
            [] => throw new InvalidOperationException(
                "The application's IConfiguration has no Storyteller source: add one with builder.Configuration.AddStoryteller(…) before the host is built."),
            var refreshers => throw new InvalidOperationException(
                $"The application's IConfiguration has {refreshers.Count} Storyteller sources: resolve {nameof(IStorytellerConfigurationRefresherProvider)} and pick one of its {nameof(Refreshers)}."),
        };
    }
}
