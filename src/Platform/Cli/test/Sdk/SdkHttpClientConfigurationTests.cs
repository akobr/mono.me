using System.Collections.Generic;
using _42.Platform.Storyteller.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Sdk;

// Sdk.NSwag has no test project of its own; sform is its only consumer.
public class SdkHttpClientConfigurationTests
{
    [Fact]
    public void ConfigureHttpClient_RunsForEachOfTheSevenTypedClients()
    {
        var clients = new List<string>();

        new ServiceCollection().AddStorytellerSdk(null, builder => clients.Add(builder.Name));

        clients.ShouldBe(
        [
            nameof(IAccessApiClient),
            nameof(IAnnotationsApiClient),
            nameof(IConfigurationsApiClient),
            nameof(ISchemasApiClient),
            nameof(IKeyValuesApiClient),
            nameof(ITemplatesApiClient),
            nameof(ITransfersApiClient),
        ]);
    }

    [Fact]
    public void WithoutConfigureHttpClient_TheClientsAreRegisteredAsBefore()
    {
        using var provider = new ServiceCollection().AddStorytellerSdk().BuildServiceProvider();

        provider.GetRequiredService<IAnnotationsApiClient>().ShouldBeOfType<AnnotationsApiClient>();
        provider.GetRequiredService<ISdkConfiguration>().ShouldBeOfType<SdkConfiguration>();
    }
}
