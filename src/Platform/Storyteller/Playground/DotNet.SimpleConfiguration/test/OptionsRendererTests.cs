using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Credentials;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Playground.DotNet.SimpleConfiguration.UnitTests;

public class OptionsRendererTests
{
    private const string FirstDocument = """{ "greeting": "Hello", "batchSize": 10, "tags": ["alpha"] }""";

    [Fact]
    public async Task Start_WritesTheKeyTheDocumentAndTheOptions()
    {
        await using var api = await FakeApi.StartAsync(FirstDocument);
        using var application = await Application.StartAsync(api);

        application.Lines.ShouldBe(
        [
            FakeApi.Key,
            "  batchSize = \"10\"",
            "  greeting = \"Hello\"",
            "  tags:0 = \"alpha\"",
            """Options: {"Greeting":"Hello","BatchSize":10,"Tags":["alpha"]}""",
        ]);
    }

    [Fact]
    public async Task Refresh_WritesTheChangedKeysAndTheNewOptions()
    {
        await using var api = await FakeApi.StartAsync(FirstDocument);
        using var application = await Application.StartAsync(api);
        application.Clear();

        api.Publish("""{ "batchSize": 20, "tags": ["alpha", "beta"], "added": { "nested": true } }""");
        (await application.Refresher.TryRefreshAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

        var lines = application.Lines;
        lines[0].ShouldEndWith("IOptionsMonitor changed");
        lines[1..^1].ShouldBe(
            [
                "  ~ batchSize = \"10\" -> \"20\"",
                "  + tags:1 = \"beta\"",
                "  + added:nested = \"true\"",
                "  - greeting = \"Hello\"",
            ],
            ignoreOrder: true);
        lines[^1].ShouldBe("""Options: {"Greeting":null,"BatchSize":20,"Tags":["alpha","beta"]}""");
    }

    [Fact]
    public async Task DocumentThatDoesNotBind_IsRolledBack_AndNoKeyChanges()
    {
        await using var api = await FakeApi.StartAsync(FirstDocument);
        using var application = await Application.StartAsync(api);
        application.Clear();

        api.Publish("""{ "greeting": "Hello", "batchSize": "abc", "tags": ["alpha"] }""");
        await application.Refresher.TryRefreshAsync(TestContext.Current.CancellationToken);

        application.Refresher.Status.IsDegraded.ShouldBeTrue();
        application.Lines[0].ShouldEndWith("IOptionsMonitor changed, no key did");
        application.Lines[^1].ShouldContain("\"BatchSize\":10");
    }

    [Fact]
    public async Task DocumentKeys_HoldTheDocumentOnly_WithTheValueTheApplicationReads()
    {
        await using var api = await FakeApi.StartAsync("""{ "fromThePlatform": "yes", "overridden": "platform" }""");
        using var application = await Application.StartAsync(api, new Dictionary<string, string?>
        {
            ["fromAppSettings"] = "local",
            ["overridden"] = "local",
        });

        var keys = DocumentKeys.Read(application.Services.GetRequiredService<IConfiguration>());

        keys.Keys.ShouldBe(["fromThePlatform", "overridden"], ignoreOrder: true);
        keys["overridden"].ShouldBe("local");
    }

    // The registrations of Program.cs, with every setting in code so the variables of the machine running the tests stay out.
    private sealed class Application : IDisposable
    {
        private readonly IHost _host;
        private readonly StringWriter _output;

        private Application(IHost host, StringWriter output, IStorytellerConfigurationRefresher refresher)
        {
            _host = host;
            _output = output;
            Refresher = refresher;
        }

        public IStorytellerConfigurationRefresher Refresher { get; }

        public IServiceProvider Services => _host.Services;

        public string[] Lines => _output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        public static async Task<Application> StartAsync(FakeApi api, IDictionary<string, string?>? later = null)
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });

            builder.Configuration.AddStoryteller(
                options =>
                {
                    options.Endpoint = api.Endpoint;
                    options.Key = FakeApi.Key;
                    options.Mode = StorytellerReadMode.Resolved;
                    options.RefreshInterval = TimeSpan.Zero;
                    options.Optional = false;
                    options.Credential = StorytellerCredential.FromApiKey("2s.machine.secret");
                },
                out var refresher);

            if (later is not null)
            {
                builder.Configuration.AddInMemoryCollection(later);
            }

            builder.Services.AddStorytellerConfiguration();
            builder.Services.Configure<PlaygroundOptions>(builder.Configuration);

            var output = new StringWriter();
            builder.Services.AddSingleton<TextWriter>(output);
            builder.Services.AddHostedService<OptionsRenderer>();

            var host = builder.Build();
            await host.StartAsync(TestContext.Current.CancellationToken);
            return new Application(host, output, refresher);
        }

        public void Clear()
        {
            _output.GetStringBuilder().Clear();
        }

        public void Dispose()
        {
            _host.Dispose();
            _output.Dispose();
        }
    }
}
