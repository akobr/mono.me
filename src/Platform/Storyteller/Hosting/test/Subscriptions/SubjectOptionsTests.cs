using System.Collections.Concurrent;
using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Informer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Subscriptions;

public sealed class SubjectOptionsTests : IDisposable
{
    private readonly HostingFixture _fixture = new();
    private readonly FakeCatalog _catalog;
    private readonly FakeConfigurations _configurations;

    public SubjectOptionsTests()
    {
        _catalog = new FakeCatalog(_fixture);
        _configurations = new FakeConfigurations(_fixture);
    }

    [Fact]
    public async Task Get_BindsTheSectionOfTheSubjectsDocument_ForTheMonitorAndTheSnapshot_OfTwoTypes()
    {
        Subscribe("northwind", port: 8080);
        Subscribe("contoso");
        await using var harness = Harness();
        await harness.StartAndPollAsync();

        var apps = harness.Services.GetRequiredService<IOptionsMonitor<AppOptions>>();
        var databases = harness.Services.GetRequiredService<IOptionsMonitor<DbOptions>>();
        using var scope = harness.Services.CreateScope();
        var snapshot = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<AppOptions>>();

        apps.Get("northwind").Name.ShouldBe("northwind");
        apps.Get("northwind").Port.ShouldBe(8080);
        apps.Get("contoso").Name.ShouldBe("contoso");
        databases.Get("northwind").Host.ShouldBe("db.northwind");
        databases.Get("contoso").Host.ShouldBe("db.contoso");
        snapshot.Get("contoso").Name.ShouldBe("contoso");
        snapshot.Get("northwind").Port.ShouldBe(8080);
    }

    [Fact]
    public async Task WithoutASection_TheWholeDocumentIsBound()
    {
        Subscribe("northwind");
        await using var harness = new SubscriptionsHarness(_fixture, hosting: builder => builder.AddSubjectOptions<DocumentOptions>());
        await harness.StartAndPollAsync();

        var options = harness.Services.GetRequiredService<IOptionsMonitor<DocumentOptions>>().Get("northwind");

        options.App.Name.ShouldBe("northwind");
        options.Db.Host.ShouldBe("db.northwind");
    }

    [Fact]
    public async Task AChangeOfOneSubject_RemovesAndNotifiesOnlyThatSubject()
    {
        Subscribe("northwind");
        Subscribe("contoso");
        await using var harness = Harness();
        await harness.StartAndPollAsync();
        var monitor = harness.Services.GetRequiredService<IOptionsMonitor<AppOptions>>();
        var northwind = monitor.Get("northwind");
        var contoso = monitor.Get("contoso");
        var heard = new ConcurrentQueue<(string? Name, string? Value)>();
        using var listening = monitor.OnChange((options, name) => heard.Enqueue((name, options.Name)));

        _configurations.Put(Execution("northwind"), Content("renamed"));
        await harness.NextPollAsync();

        heard.ShouldBe([("northwind", "renamed")]);
        monitor.Get("northwind").ShouldNotBeSameAs(northwind);
        monitor.Get("northwind").Name.ShouldBe("renamed");
        monitor.Get("contoso").ShouldBeSameAs(contoso);
    }

    [Fact]
    public async Task AnAddedSubject_IsNotified_AndARemovedOne_IsDroppedFromTheCache_WithoutANotification()
    {
        Subscribe("northwind");
        await using var harness = Harness();
        await harness.StartAndPollAsync();
        var monitor = harness.Services.GetRequiredService<IOptionsMonitor<AppOptions>>();
        monitor.Get("northwind").Name.ShouldBe("northwind");
        var heard = new ConcurrentQueue<(string? Name, string? Value)>();
        using var listening = monitor.OnChange((options, name) => heard.Enqueue((name, options.Name)));

        Subscribe("contoso");
        await harness.NextPollAsync();
        heard.ShouldBe([("contoso", "contoso")]);

        _catalog.Purge(Execution("northwind"));
        await harness.NextPollAsync();

        heard.ShouldBe([("contoso", "contoso")]);
        Should.Throw<OptionsValidationException>(() => monitor.Get("northwind"));
    }

    [Fact]
    public async Task ADisposedListener_HearsNothingMore()
    {
        Subscribe("northwind");
        await using var harness = Harness();
        await harness.StartAndPollAsync();
        var monitor = harness.Services.GetRequiredService<IOptionsMonitor<AppOptions>>();
        var heard = new ConcurrentQueue<string?>();
        var listening = monitor.OnChange((_, name) => heard.Enqueue(name))!;

        listening.Dispose();
        _configurations.Put(Execution("northwind"), Content("renamed"));
        await harness.NextPollAsync();

        heard.ShouldBeEmpty();
        monitor.Get("northwind").Name.ShouldBe("renamed");
    }

    [Fact]
    public async Task AnUnknownSubject_FailsValidation_AlsoBeforeTheSubscriptionsAreReady()
    {
        Subscribe("northwind");
        await using var harness = Harness();
        var monitor = harness.Services.GetRequiredService<IOptionsMonitor<AppOptions>>();

        Should.Throw<OptionsValidationException>(() => monitor.Get("northwind")).Message.ShouldBe(
            "There are no AppOptions of the subject northwind: no subscription of the subject northwind in invoicing/prod (not ready yet, pending, or not subscribed).");

        await harness.StartAndPollAsync();
        monitor.Get("northwind").Name.ShouldBe("northwind");
        Should.Throw<OptionsValidationException>(() => monitor.Get("fabrikam")).OptionsName.ShouldBe("fabrikam");
        using var scope = harness.Services.CreateScope();
        Should.Throw<OptionsValidationException>(() => scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<AppOptions>>().Get("fabrikam"));
    }

    [Fact]
    public async Task TheDefaultName_IsUntouched_AndConfigureAllReachesTheSubjects()
    {
        Subscribe("northwind");
        await using var harness = Harness(services: services => services
            .Configure<AppOptions>(options => options.Name = "default")
            .ConfigureAll<AppOptions>(options => options.Shared = "all"));
        await harness.StartAndPollAsync();
        var monitor = harness.Services.GetRequiredService<IOptionsMonitor<AppOptions>>();

        harness.Services.GetRequiredService<IOptions<AppOptions>>().Value.Name.ShouldBe("default");
        monitor.CurrentValue.Name.ShouldBe("default");
        monitor.CurrentValue.Shared.ShouldBe("all");
        monitor.Get("northwind").Name.ShouldBe("northwind");
        monitor.Get("northwind").Shared.ShouldBe("all");
    }

    [Fact]
    public async Task AValueThatDoesNotConvert_Fails_WithoutTheValue()
    {
        _catalog.Subscribe("northwind");
        _configurations.Put(Execution("northwind"), """{ "App": { "Name": "northwind", "Port": "S3cr3t" } }""");
        await using var harness = Harness();
        await harness.StartAndPollAsync();

        var exception = Should.Throw<StorytellerConfigurationException>(() => harness.Services.GetRequiredService<IOptionsMonitor<AppOptions>>().Get("northwind"));

        exception.Message.ShouldBe(
            $"The configuration {FakeCatalog.Full(Execution("northwind"))} of the subject northwind does not bind to AppOptions at App: the binder threw InvalidOperationException.");
        exception.InnerException.ShouldBeNull();
    }

    [Fact]
    public async Task AChangeThatDoesNotBind_RaisesNothing_AndWarnsWithoutTheValue()
    {
        Subscribe("northwind");
        await using var harness = Harness();
        await harness.StartAndPollAsync();
        var monitor = harness.Services.GetRequiredService<IOptionsMonitor<AppOptions>>();
        var heard = new ConcurrentQueue<string?>();
        using var listening = monitor.OnChange((_, name) => heard.Enqueue(name));

        _configurations.Put(Execution("northwind"), """{ "App": { "Name": "northwind", "Port": "S3cr3t" } }""");
        await harness.NextPollAsync();

        heard.ShouldBeEmpty();
        var warning = _fixture.Logs.Entries.Where(entry => entry.Level == LogLevel.Warning).ShouldHaveSingleItem();
        warning.Message.ShouldStartWith("The AppOptions of the subject northwind failed after Changed with StorytellerConfigurationException, so OnChange was not raised.");
        _fixture.Logs.Entries.ShouldAllBe(entry => !entry.Message.Contains("S3cr3t"));
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }

    private static string Execution(string subject)
    {
        return FakeCatalog.ExecutionKey(subject);
    }

    private static string Content(string name, int port = 80)
    {
        return $$"""{ "App": { "Name": "{{name}}", "Port": {{port}} }, "Db": { "Host": "db.{{name}}" } }""";
    }

    private SubscriptionsHarness Harness(Action<IServiceCollection>? services = null)
    {
        return new SubscriptionsHarness(
            _fixture,
            hosting: builder => builder.AddSubjectOptions<AppOptions>("App").AddSubjectOptions<DbOptions>("Db"),
            services: services);
    }

    private void Subscribe(string subject, int port = 80)
    {
        _catalog.Subscribe(subject);
        _configurations.Put(Execution(subject), Content(subject, port));
    }

    public sealed class AppOptions
    {
        public string? Name { get; set; }

        public int Port { get; set; }

        public string? Shared { get; set; }
    }

    public sealed class DbOptions
    {
        public string? Host { get; set; }
    }

    public sealed class DocumentOptions
    {
        public AppOptions App { get; set; } = new();

        public DbOptions Db { get; set; } = new();
    }
}
