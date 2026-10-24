using _42.Platform.Storyteller.Sdk.Configuring.Engine;
using Microsoft.Extensions.Configuration;

namespace _42.Platform.Storyteller.Sdk.Configuring;

// Added by AddStoryteller. Holds the engine, so every provider it builds shares one poller, one cache writer and one
// refresher, however often its builder builds.
public sealed class StorytellerConfigurationSource : IConfigurationSource
{
    internal StorytellerConfigurationSource(StorytellerConfigurationEngine engine)
    {
        Engine = engine;
    }

    internal StorytellerConfigurationEngine Engine { get; }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new StorytellerConfigurationProvider(Engine);
    }
}
