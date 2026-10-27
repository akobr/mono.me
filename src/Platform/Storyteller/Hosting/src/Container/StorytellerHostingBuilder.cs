using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Sdk.Hosting.Container;

internal sealed class StorytellerHostingBuilder : IStorytellerHostingBuilder
{
    public StorytellerHostingBuilder(IServiceCollection services)
    {
        Services = services;
    }

    public IServiceCollection Services { get; }
}
