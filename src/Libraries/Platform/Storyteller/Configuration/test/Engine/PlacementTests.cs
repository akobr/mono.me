using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Hosting;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Engine;

public sealed class PlacementTests : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly CountingTimeProvider _time = new();
    private readonly EngineFixture _fixture;

    public PlacementTests()
    {
        _fixture = new EngineFixture(_time);
    }

    [Fact]
    public void InAConfigurationManagerSetUpLikeAHost_ItGoesBeforeTheEnvironment_WhichStillOverridesIt()
    {
        // A name of its own: the environment of the process is shared by every test.
        var section = $"Placement{Guid.NewGuid():N}";
        var variable = $"{section}__Value";
        Environment.SetEnvironmentVariable(variable, "environment");

        try
        {
            var manager = _fixture.Track(new ConfigurationManager());
            manager.AddEnvironmentVariables("DOTNET_");
            manager.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{section}:Value"] = "appsettings",
                [$"{section}:Other"] = "appsettings",
            });
            manager.AddEnvironmentVariables();
            manager.AddCommandLine([]);
            _fixture.Http.Respond(
                HttpStatusCode.OK,
                TestDocuments.Json($$"""{ "{{section}}": { "Value": "platform", "Other": "platform" } }"""),
                ("ETag", EngineFixture.Tag(1)));

            _fixture.Add(manager);

            // A ConfigurationManager starts with a memory source of its own.
            var sources = ((IConfigurationBuilder)manager).Sources;
            sources.Select(source => source.GetType()).ShouldBe(
            [
                typeof(MemoryConfigurationSource),
                typeof(EnvironmentVariablesConfigurationSource),
                typeof(MemoryConfigurationSource),
                typeof(StorytellerConfigurationSource),
                typeof(EnvironmentVariablesConfigurationSource),
                typeof(CommandLineConfigurationSource),
            ]);
            sources[4].ShouldBeOfType<EnvironmentVariablesConfigurationSource>().Prefix.ShouldBeNullOrEmpty();
            manager[$"{section}:Value"].ShouldBe("environment");
            manager[$"{section}:Other"].ShouldBe("platform");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void InAHostApplicationBuilder_ItGoesRightBeforeTheLastUnprefixedEnvironmentSource()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
        _fixture.Track((IDisposable)builder.Configuration);
        _fixture.RespondDocument("one", EngineFixture.Tag(1));

        _fixture.Add(builder.Configuration);

        var sources = ((IConfigurationBuilder)builder.Configuration).Sources;
        var index = sources.IndexOf(sources.OfType<StorytellerConfigurationSource>().Single());
        sources[index + 1].ShouldBeOfType<EnvironmentVariablesConfigurationSource>().Prefix.ShouldBeNullOrEmpty();
        sources.Skip(index + 2).ShouldNotContain(source => source is EnvironmentVariablesConfigurationSource);
        builder.Configuration["App:Name"].ShouldBe("one");
    }

    [Fact]
    public void WithoutAnUnprefixedEnvironmentSource_ItIsAppended()
    {
        var builder = new ConfigurationBuilder()
            .AddEnvironmentVariables("DOTNET_")
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:Name"] = "appsettings" });
        _fixture.RespondDocument("one", EngineFixture.Tag(1));

        _fixture.Add(builder);

        builder.Sources[^1].ShouldBeOfType<StorytellerConfigurationSource>();
        _fixture.Track(builder.Build())["App:Name"].ShouldBe("one");
    }

    [Fact]
    public void Last_IsAppended_SoNothingLocalOverridesThePlatform()
    {
        var builder = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:Name"] = "local" });
        _fixture.RespondDocument("one", EngineFixture.Tag(1));

        _fixture.Add(builder, options => options.Placement = SourcePlacement.Last);

        builder.Sources[^1].ShouldBeOfType<StorytellerConfigurationSource>();
        _fixture.Track(builder.Build())["App:Name"].ShouldBe("one");
    }

    [Fact]
    public void TheOptions_AreValidatedWhenTheSourceIsAdded()
    {
        var builder = new ConfigurationBuilder();
        _fixture.Environment.Remove("STORYTELLER_KEY");

        Should.Throw<StorytellerConfigurationException>(() => _fixture.Add(builder)).Message.ShouldContain("STORYTELLER_KEY");

        builder.Sources.ShouldBeEmpty();
        _fixture.Http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task AConfigurationManagerRebuild_SharesOneEngineAndOnePoller()
    {
        var manager = _fixture.Track(new ConfigurationManager());
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var engine = _fixture.Add(manager);

        // Builds and loads every source again, and disposes the old providers after.
        ((IConfigurationBuilder)manager).Sources.Insert(0, new MemoryConfigurationSource());

        manager["App:Name"].ShouldBe("one");
        ((IConfigurationRoot)manager).Providers.OfType<StorytellerConfigurationProvider>().ShouldHaveSingleItem();
        _fixture.Http.Requests.Count.ShouldBe(1);
        _time.Active.ShouldBe(1);
        engine.IsRunning.ShouldBeTrue();

        _fixture.RespondDocument("two", EngineFixture.Tag(2), version: 4);
        await _fixture.AdvanceAsync(engine, Interval);

        _fixture.Http.Requests.Count.ShouldBe(2);
        manager["App:Name"].ShouldBe("two");
    }

    [Fact]
    public async Task TwoBuilds_ShareOneEngineAndOnePoller()
    {
        var builder = new ConfigurationBuilder();
        var engine = _fixture.Add(builder);
        _fixture.RespondDocument("one", EngineFixture.Tag(1));

        var first = _fixture.Track(builder.Build());
        var second = _fixture.Track(builder.Build());

        _fixture.Http.Requests.Count.ShouldBe(1);
        _time.Created.ShouldBe(1);

        _fixture.RespondDocument("two", EngineFixture.Tag(2), version: 4);
        await _fixture.AdvanceAsync(engine, Interval);

        _fixture.Http.Requests.Count.ShouldBe(2);
        first["App:Name"].ShouldBe("two");
        second["App:Name"].ShouldBe("two");
        _time.Active.ShouldBe(1);
    }

    [Fact]
    public void ASourceAddedAfterStoryteller_IsBuiltAndLoadedOnce()
    {
        var manager = _fixture.Track(new ConfigurationManager());
        manager.AddEnvironmentVariables();
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        _fixture.Add(manager);
        var counting = new CountingSource();

        ((IConfigurationBuilder)manager).Add(counting);

        counting.Builds.ShouldBe(1);
        counting.Loads.ShouldBe(1);
        _fixture.Http.Requests.Count.ShouldBe(1);
        manager["App:Name"].ShouldBe("one");
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }

    private sealed class CountingSource : IConfigurationSource
    {
        private int _builds;
        private int _loads;

        public int Builds => Volatile.Read(ref _builds);

        public int Loads => Volatile.Read(ref _loads);

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            Interlocked.Increment(ref _builds);
            return new CountingProvider(this);
        }

        private sealed class CountingProvider : ConfigurationProvider
        {
            private readonly CountingSource _source;

            public CountingProvider(CountingSource source)
            {
                _source = source;
            }

            public override void Load()
            {
                Interlocked.Increment(ref _source._loads);
            }
        }
    }
}
