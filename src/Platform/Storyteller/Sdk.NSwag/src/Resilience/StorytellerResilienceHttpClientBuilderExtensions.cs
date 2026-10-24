using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace _42.Platform.Storyteller.Sdk.Resilience;

public static class StorytellerResilienceHttpClientBuilderExtensions
{
    // Opt-in: AddStorytellerSdk adds no resilience. Not together with AddStandardResilienceHandler (Aspire's service
    // defaults add it to every client), which would retry the retries.
    public static IHttpClientBuilder AddStorytellerResilience(this IHttpClientBuilder builder, Action<StorytellerResilienceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new StorytellerResilienceOptions();
        configure?.Invoke(options);

        // One pipeline for every handler the factory creates, so the breaker outlives a handler rotation.
        var pipeline = StorytellerResilience.CreatePipeline(options);
        return builder.AddHttpMessageHandler(() => new ResilienceHandler(pipeline));
    }
}
