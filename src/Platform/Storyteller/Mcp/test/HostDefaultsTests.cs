using _42.Platform.Storyteller.Mcp.Authentication;
using _42.Platform.Storyteller.Mcp.Hosting;
using _42.Platform.Storyteller.Mcp.Tools;
using _42.Platform.Storyteller.Mcp.UnitTests.Support;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

// Process and client settings the host makes before it serves a call.
public sealed class HostDefaultsTests : IDisposable
{
    private readonly McpTestHost _host = new();

    [Fact]
    public void Host_SetsTheDefaultRegexMatchTimeout()
    {
        _host.CreateClient().Dispose();

        AppContext.GetData("REGEX_DEFAULT_MATCH_TIMEOUT").ShouldBe(TimeSpan.FromMilliseconds(200));
    }

    [Theory]
    [InlineData(nameof(Sdk.IAccessApiClient))]
    [InlineData(nameof(Sdk.IKeyValuesApiClient))]
    [InlineData(nameof(Sdk.IAnnotationsApiClient))]
    public void SdkClients_WaitForTheApiAsLongAsALongWriteTakes(string client)
    {
        _host.CreateClient().Dispose();

        // Get runs the options validators, the one that compares the circuit breaker with the attempt timeout included.
        var options = _host.Services.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>().Get($"{client}-standard");
        using var http = _host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(client);

        options.AttemptTimeout.Timeout.ShouldBe(McpHostExtensions.ApiCallTimeout);
        options.TotalRequestTimeout.Timeout.ShouldBe(McpHostExtensions.ApiCallTimeout);
        options.CircuitBreaker.SamplingDuration.ShouldBeGreaterThanOrEqualTo(2 * McpHostExtensions.ApiCallTimeout);
        http.Timeout.ShouldBe(Timeout.InfiniteTimeSpan);
    }

    // In production the Container Apps ingress drops a request after 240 seconds without a byte, the start of a replica
    // included (the startup probe allows about 31 seconds), and the API's front end answers 502 after 230 seconds. A tool
    // call ends before either, with room for the start of a replica.
    [Fact]
    public void ToolCallDeadline_EndsBeforeTheIngressAndTheApisFrontEnd()
    {
        McpHostExtensions.ApiCallTimeout.ShouldBe(StorytellerCallFilter.ToolCallTimeout);
        StorytellerCallFilter.ToolCallTimeout.ShouldBeLessThan(TimeSpan.FromSeconds(230));
        StorytellerCallFilter.ToolCallTimeout.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(240) - TimeSpan.FromSeconds(40));
        StorytellerTokenExchangeProvider.ExchangeTimeout.ShouldBeLessThan(StorytellerCallFilter.ToolCallTimeout);
    }

    public void Dispose()
    {
        _host.Dispose();
    }
}
