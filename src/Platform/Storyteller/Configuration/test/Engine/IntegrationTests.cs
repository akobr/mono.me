using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Engine;

public sealed class IntegrationTests : IDisposable
{
    private readonly EngineFixture _fixture = new();

    [Fact]
    public async Task AnOptionsMonitor_SeesAnAppliedChange()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build();
        var services = new ServiceCollection();
        services.Configure<RefreshTests.AppOptions>(root.GetSection("App"));
        using var container = services.BuildServiceProvider();
        var monitor = container.GetRequiredService<IOptionsMonitor<RefreshTests.AppOptions>>();
        var names = new List<string?>();
        using var listener = monitor.OnChange(options => names.Add(options.Name));
        monitor.CurrentValue.Name.ShouldBe("one");
        _fixture.RespondDocument("two", EngineFixture.Tag(2), version: 4, port: 8080);

        (await engine.TryRefreshAsync()).ShouldBeTrue();

        names.ShouldBe(["two"]);
        monitor.CurrentValue.Port.ShouldBe(8080);
    }

    [Fact]
    public void AddStorytellerConfiguration_FindsTheRefresher_AlsoThroughAChainedConfiguration()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (inner, engine) = _fixture.Build();
        var outer = _fixture.Track(new ConfigurationBuilder().AddConfiguration(inner).Build());
        var services = new ServiceCollection().AddSingleton<IConfiguration>(outer);

        services.AddStorytellerConfiguration();
        services.AddStorytellerConfiguration();

        using var container = services.BuildServiceProvider();
        container.GetRequiredService<IStorytellerConfigurationRefresherProvider>().Refreshers.ShouldHaveSingleItem().ShouldBeSameAs(engine);
        container.GetRequiredService<IStorytellerConfigurationRefresher>().ShouldBeSameAs(engine);
        services.Count(service => service.ServiceType == typeof(IHostedService)).ShouldBe(1);
    }

    [Fact]
    public void TheRefresherProvider_FollowsAConfigurationManager_AndTheSingleRefresherNeedsExactlyOne()
    {
        var manager = _fixture.Track(new ConfigurationManager());
        var services = new ServiceCollection().AddSingleton<IConfiguration>(manager).AddStorytellerConfiguration();
        using var container = services.BuildServiceProvider();
        var refreshers = container.GetRequiredService<IStorytellerConfigurationRefresherProvider>();

        Should.Throw<InvalidOperationException>(() => container.GetRequiredService<IStorytellerConfigurationRefresher>())
            .Message.ShouldContain("no Storyteller source");

        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var first = _fixture.Add(manager);
        refreshers.Refreshers.ShouldBe([first]);
        container.GetRequiredService<IStorytellerConfigurationRefresher>().ShouldBeSameAs(first);

        _fixture.RespondDocument("other", EngineFixture.Tag(2));
        var second = _fixture.Add(manager, options => options.Key = "@acme.billing.default.sbt.contoso");
        refreshers.Refreshers.ShouldBe([first, second]);
        Should.Throw<InvalidOperationException>(() => container.GetRequiredService<IStorytellerConfigurationRefresher>())
            .Message.ShouldContain(nameof(IStorytellerConfigurationRefresherProvider));
    }

    [Fact]
    public async Task ADegradedStart_IsLoggedOnceTheHostStarts_AndLoggingRevertsAtStop()
    {
        var logs = new RecordingLoggerFactory();
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        _fixture.Http.Respond(HttpStatusCode.ServiceUnavailable);
        builder.Configuration.AddStoryteller(_fixture.Configure(options => options.Optional = true, withLogs: false), out var refresher);
        builder.Services.AddSingleton<ILoggerFactory>(logs);
        builder.Services.AddStorytellerConfiguration();
        using var host = builder.Build();

        Ours(logs).ShouldBeEmpty();

        await host.StartAsync();

        var degraded = Ours(logs).ShouldHaveSingleItem();
        degraded.Level.ShouldBe(LogLevel.Warning);
        degraded.Message.ShouldContain("degraded");
        degraded.Message.ShouldContain(TestDocuments.Key);

        // While the host runs, a failed refresh is logged.
        _fixture.Http.Respond(HttpStatusCode.ServiceUnavailable);
        (await refresher.TryRefreshAsync()).ShouldBeFalse();
        Ours(logs).Count.ShouldBe(2);

        await host.StopAsync();
        _fixture.Http.Respond(HttpStatusCode.ServiceUnavailable);
        (await refresher.TryRefreshAsync()).ShouldBeFalse();

        Ours(logs).Count.ShouldBe(2);
    }

    [Fact]
    public async Task AGivenLoggerFactory_IsKeptOverTheContainers()
    {
        var hostLogs = new RecordingLoggerFactory();
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        _fixture.Http.Respond(HttpStatusCode.ServiceUnavailable);
        builder.Configuration.AddStoryteller(_fixture.Configure(options => options.Optional = true), out var refresher);
        builder.Services.AddSingleton<ILoggerFactory>(hostLogs);
        builder.Services.AddStorytellerConfiguration();
        using var host = builder.Build();

        await host.StartAsync();
        _fixture.Http.Respond(HttpStatusCode.ServiceUnavailable);
        (await refresher.TryRefreshAsync()).ShouldBeFalse();
        await host.StopAsync();

        Ours(hostLogs).ShouldBeEmpty();
        _fixture.Logs.Entries.Count(entry => entry.Level == LogLevel.Warning).ShouldBe(2);
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }

    // The host logs too.
    private static List<RecordingLoggerFactory.LogEntry> Ours(RecordingLoggerFactory logs)
    {
        return [.. logs.Entries.Where(entry => entry.Category.StartsWith("_42.Platform.Storyteller", StringComparison.Ordinal))];
    }
}
