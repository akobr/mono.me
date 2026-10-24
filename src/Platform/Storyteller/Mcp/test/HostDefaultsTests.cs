using _42.Platform.Storyteller.Mcp.Hosting;
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

    public void Dispose()
    {
        _host.Dispose();
    }
}
