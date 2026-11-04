using System.Diagnostics;
using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Hosting.Catalog;
using _42.Platform.Storyteller.Sdk.Hosting.Container;
using _42.Platform.Storyteller.Sdk.Hosting.Informer;
using _42.Platform.Storyteller.Sdk.Hosting.Reading;
using _42.Platform.Storyteller.Sdk.Hosting.Subscriptions;
using _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Informer;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Subscriptions;

// The subscriptions of the fake catalog's responsibility and context over the fixture, in a container of their own,
// started as a host starts them. A poll is over when the source armed the timer of the next one, which it does only
// after the loop read the documents and published, so the clock is advanced only then. The loop must be the only one
// that makes timers of the clock.
internal sealed class SubscriptionsHarness : IAsyncDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly HostingFixture _fixture;

    // hosting: more calls on the builder, after AddSubscriptions. source: a source over the polling one, for a service
    // of its own built as the registration builds it; the registered one is then left alone.
    public SubscriptionsHarness(
        HostingFixture fixture,
        Action<StorytellerSubscriptionOptions>? configure = null,
        Action<IStorytellerHostingBuilder>? hosting = null,
        Action<StorytellerConfigurationOptions>? options = null,
        Action<IServiceCollection>? services = null,
        Func<ISubscriptionChangeSource, ISubscriptionChangeSource>? source = null)
    {
        _fixture = fixture;
        var collection = new ServiceCollection();
        services?.Invoke(collection);
        var builder = collection
            .AddStoryteller(fixture.Configure(options))
            .AddSubscriptions(FakeCatalog.Responsibility, FakeCatalog.Context, configure);
        hosting?.Invoke(builder);
        Services = fixture.Track(collection.BuildServiceProvider());

        if (source is null)
        {
            Service = Services.GetRequiredService<SubscriptionsService>();
            return;
        }

        var settings = Services.GetRequiredService<HostingSettings>().Settings;
        var subscription = SubscriptionSettings.Create(settings, FakeCatalog.Responsibility, FakeCatalog.Context, configure);
        var polling = new PollingSubscriptionSource(
            Services.GetRequiredService<CatalogReader>(),
            subscription.PollInterval,
            subscription.MaxConcurrentReads,
            settings.TimeProvider,
            settings.NextRandom,
            fixture.Logs.CreateLogger(SubscriptionCatalog.InformerLogCategory));
        Service = fixture.Track(new SubscriptionsService(subscription, settings, source(polling), null));
    }

    public ServiceProvider Services { get; }

    public SubscriptionsService Service { get; }

    public IStorytellerSubscriptions Subscriptions => Service;

    public Task StartAsync()
    {
        return Service.StartAsync(CancellationToken.None);
    }

    // Starts the loop and waits until its first poll was published.
    public async Task StartAndPollAsync()
    {
        await StartAsync();
        await WaitAsync(() => _fixture.Time.Created >= 1);
    }

    // Advances the clock and waits until the poll it started was published.
    public async Task NextPollAsync(TimeSpan? advance = null)
    {
        var timers = _fixture.Time.Created;
        _fixture.Time.Advance(advance ?? Interval);
        await WaitAsync(() => _fixture.Time.Created > timers);
    }

    public async ValueTask DisposeAsync()
    {
        using var patience = new CancellationTokenSource(HostingFixture.Patience);
        await Service.StopAsync(patience.Token);
    }

    // A loop that ended unexpectedly fails the wait.
    private async Task WaitAsync(Func<bool> isOver)
    {
        var waiting = Stopwatch.StartNew();

        while (!isOver())
        {
            if (Service.ExecuteTask is { IsCompleted: true } ended)
            {
                await ended;
                throw new InvalidOperationException("The loop of the subscriptions ended.");
            }

            if (waiting.Elapsed > HostingFixture.Patience)
            {
                throw new TimeoutException("The poll did not end.");
            }

            await Task.Delay(5);
        }
    }
}
