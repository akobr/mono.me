using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Net.Http.Headers;
using _42.Platform.Storyteller.Sdk.Configuring.Caching;
using _42.Platform.Storyteller.Sdk.Configuring.Engine;
using _42.Platform.Storyteller.Sdk.Configuring.Reading;
using _42.Platform.Storyteller.Sdk.Credentials;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Engine;

public sealed class StartupTests : IDisposable
{
    private readonly EngineFixture _fixture = new();

    public enum Scenario
    {
        Unreachable,
        UnreachableWithCache,
        NotFound,
        NotFoundWithCache,
        Unauthorized,
        Withheld,
        InvalidDocument,
        CredentialConfiguration,
        CredentialRejected,
        CredentialUnavailable,
    }

    // The table of section 11 without the resolver rows: what an outcome does to a required and an optional provider.
    // null: Load() throws.
    public static TheoryData<Scenario, bool, StorytellerConfigurationOrigin?, bool> Table()
    {
        return new TheoryData<Scenario, bool, StorytellerConfigurationOrigin?, bool>
        {
            { Scenario.Unreachable, false, null, true },
            { Scenario.Unreachable, true, StorytellerConfigurationOrigin.Empty, true },
            { Scenario.UnreachableWithCache, false, StorytellerConfigurationOrigin.Cache, true },
            { Scenario.UnreachableWithCache, true, StorytellerConfigurationOrigin.Cache, true },
            { Scenario.NotFound, false, null, false },
            { Scenario.NotFound, true, null, false },
            { Scenario.NotFoundWithCache, false, null, false },
            { Scenario.NotFoundWithCache, true, null, false },
            { Scenario.Unauthorized, true, null, false },
            { Scenario.Withheld, false, null, false },
            { Scenario.Withheld, true, null, false },
            { Scenario.InvalidDocument, false, null, false },
            { Scenario.InvalidDocument, true, null, false },
            { Scenario.CredentialConfiguration, true, null, false },
            { Scenario.CredentialRejected, true, null, false },
            { Scenario.CredentialUnavailable, false, null, true },
            { Scenario.CredentialUnavailable, true, StorytellerConfigurationOrigin.Empty, true },
        };
    }

    [Fact]
    public void A200_StartsFromThePlatform()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));

        var (root, engine) = _fixture.Build();

        root["App:Name"].ShouldBe("one");
        root["app:port"].ShouldBe("80");
        _fixture.Http.Requests.ShouldHaveSingleItem().Headers.ContainsKey("If-None-Match").ShouldBeFalse();

        var status = engine.Status;
        status.ActiveKey.ShouldBe(TestDocuments.Key);
        status.ETag.ShouldBe(EngineFixture.Tag(1));
        status.Origin.ShouldBe(StorytellerConfigurationOrigin.Platform);
        status.IsDegraded.ShouldBeFalse();
        status.LastAppliedAt.ShouldBe(_fixture.Time.GetUtcNow());
        status.LastSucceededAt.ShouldBe(_fixture.Time.GetUtcNow());
        status.LastError.ShouldBeNull();
        status.ConsecutiveFailures.ShouldBe(0);
    }

    [Fact]
    public void BeforeTheFirstLoad_TheStatusNamesTheConfiguredKey_AndIsDegraded()
    {
        var engine = _fixture.Add(new ConfigurationBuilder(), options => options.Key = "@acme.billing.default.SBT.northwind");

        engine.ActiveKey.ShouldBe(TestDocuments.Key);
        var status = engine.Status;
        status.Origin.ShouldBe(StorytellerConfigurationOrigin.Empty);
        status.IsDegraded.ShouldBeTrue();
        status.LastAppliedAt.ShouldBeNull();
        status.ETag.ShouldBeNull();
        _fixture.Http.Requests.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Table))]
    public void TheStartupTable(Scenario scenario, bool optional, StorytellerConfigurationOrigin? origin, bool isTransient)
    {
        var withCache = scenario is Scenario.UnreachableWithCache or Scenario.NotFoundWithCache;

        if (withCache)
        {
            SeedCache("cached");
        }

        Arrange(scenario);

        var builder = new ConfigurationBuilder();
        var engine = _fixture.Add(builder, options =>
        {
            options.Optional = optional;
            options.CacheDirectory = withCache ? EngineFixture.CacheDirectory : null;
            options.Credential = scenario switch
            {
                Scenario.CredentialConfiguration => TestCredential.Failing(StorytellerAuthenticationFailureReason.Configuration),
                Scenario.CredentialRejected => TestCredential.Failing(StorytellerAuthenticationFailureReason.Rejected),
                Scenario.CredentialUnavailable => TestCredential.Failing(StorytellerAuthenticationFailureReason.Unavailable),
                _ => null,
            };
        });

        if (origin is null)
        {
            var exception = Should.Throw<StorytellerConfigurationException>(() => _fixture.Track(builder.Build()));
            exception.IsTransient.ShouldBe(isTransient);
            exception.Key.ShouldBe(TestDocuments.Key);

            engine.Status.LastError.ShouldBeSameAs(exception);
            engine.Status.ConsecutiveFailures.ShouldBe(1);
            return;
        }

        var root = _fixture.Track(builder.Build());
        var status = engine.Status;

        status.Origin.ShouldBe(origin.Value);
        status.IsDegraded.ShouldBeTrue();
        status.LastError.ShouldNotBeNull().IsTransient.ShouldBe(isTransient);
        status.ConsecutiveFailures.ShouldBe(1);
        status.TotalFailures.ShouldBe(1);
        status.ETag.ShouldBeNull();
        status.LastAppliedAt.ShouldBe(origin == StorytellerConfigurationOrigin.Cache ? _fixture.Time.GetUtcNow() : null);
        root["App:Name"].ShouldBe(origin == StorytellerConfigurationOrigin.Cache ? "cached" : null);
    }

    [Fact]
    public async Task ADegradedStart_KeepsPolling_AndRecovers()
    {
        _fixture.Http.Respond(HttpStatusCode.ServiceUnavailable);
        _fixture.RespondDocument("one", EngineFixture.Tag(1));

        var (root, engine) = _fixture.Build(options => options.Optional = true);

        engine.Status.Origin.ShouldBe(StorytellerConfigurationOrigin.Empty);

        await _fixture.AdvanceAsync(engine, TimeSpan.FromSeconds(30));

        root["App:Name"].ShouldBe("one");
        engine.Status.Origin.ShouldBe(StorytellerConfigurationOrigin.Platform);
        engine.Status.IsDegraded.ShouldBeFalse();
        engine.Status.ConsecutiveFailures.ShouldBe(0);
        engine.Status.TotalFailures.ShouldBe(1);
    }

    [Fact]
    public void AProtectedCacheEntry_SendsItsTag_AndA304AppliesIt()
    {
        var protector = new TestCacheProtector();
        SeedCache("cached", protector);
        _fixture.RespondNotModified(EngineFixture.Tag(1));

        var (root, engine) = _fixture.Build(options =>
        {
            options.CacheDirectory = EngineFixture.CacheDirectory;
            options.CacheProtector = protector;
        });

        root["App:Name"].ShouldBe("cached");
        _fixture.Http.Requests[^1].Headers["If-None-Match"].ShouldBe(EngineFixture.Tag(1));
        engine.Status.Origin.ShouldBe(StorytellerConfigurationOrigin.Platform);
        engine.Status.IsDegraded.ShouldBeFalse();
        engine.Status.ETag.ShouldBe(EngineFixture.Tag(1));
    }

    [Fact]
    public void APlainCacheEntry_IsNeverConfirmed_ButStandsInWhenThePlatformIsUnreachable()
    {
        SeedCache("cached");
        _fixture.Http.Respond(HttpStatusCode.BadGateway);

        var (root, engine) = _fixture.Build(options => options.CacheDirectory = EngineFixture.CacheDirectory);

        root["App:Name"].ShouldBe("cached");
        _fixture.Http.Requests[^1].Headers.ContainsKey("If-None-Match").ShouldBeFalse();
        engine.Status.Origin.ShouldBe(StorytellerConfigurationOrigin.Cache);
        engine.Status.ETag.ShouldBeNull();
    }

    [Fact]
    public async Task ConcurrentFirstLoads_ShareOneStartup()
    {
        var gate = new GatedResponse(() => EngineFixture.Document("one", EngineFixture.Tag(1)));
        _fixture.Http.Respond(gate.RespondAsync);

        var builder = new ConfigurationBuilder();
        _fixture.Add(builder);

        var first = Task.Run(() => _fixture.Track(builder.Build()));
        await gate.Entered;

        IConfigurationRoot? second = null;
        var thread = new Thread(() => second = _fixture.Track(builder.Build()));
        thread.Start();

        // The second load waits for the startup the first one runs.
        SpinWait.SpinUntil(() => thread.ThreadState.HasFlag(ThreadState.WaitSleepJoin), EngineFixture.Patience).ShouldBeTrue();
        gate.Release();

        thread.Join(EngineFixture.Patience).ShouldBeTrue();
        (await first.WaitAsync(EngineFixture.Patience))["App:Name"].ShouldBe("one");
        second.ShouldNotBeNull()["App:Name"].ShouldBe("one");
        _fixture.Http.Requests.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AHandlerThatNeverAnswers_EndsTheStartupWithinReadTimeout_AsTransient(bool honoursCancellation)
    {
        _fixture.Http.Respond(async (_, cancellationToken) =>
        {
            await (honoursCancellation ? Task.Delay(Timeout.Infinite, cancellationToken) : new TaskCompletionSource().Task);
            return StubHttpHandler.Response(HttpStatusCode.OK);
        });
        _fixture.Http.Respond(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return StubHttpHandler.Response(HttpStatusCode.OK);
        });

        var readTimeout = TimeSpan.FromMilliseconds(200);
        var builder = new ConfigurationBuilder();
        var engine = _fixture.Add(builder, options =>
        {
            options.ReadTimeout = readTimeout;
            options.Optional = true;
            options.Resilience.AttemptTimeout = Timeout.InfiniteTimeSpan;
        });
        var started = DateTime.UtcNow;

        var root = _fixture.Track(builder.Build());

        (DateTime.UtcNow - started).ShouldBeLessThan(EngineFixture.Patience);
        root["App:Name"].ShouldBeNull();
        engine.Status.Origin.ShouldBe(StorytellerConfigurationOrigin.Empty);
        var error = engine.Status.LastError.ShouldNotBeNull();
        error.IsTransient.ShouldBeTrue();
        error.Message.ShouldContain("ReadTimeout");

        // A required provider throws it.
        var required = new ConfigurationBuilder();
        _fixture.Add(required, options =>
        {
            options.ReadTimeout = readTimeout;
            options.Resilience.AttemptTimeout = Timeout.InfiniteTimeSpan;
        });

        Should.Throw<StorytellerConfigurationException>(() => _fixture.Track(required.Build())).IsTransient.ShouldBeTrue();
    }

    // The credential's token source hangs, and the read with it; the cache stands in within ReadTimeout, which bounds
    // the fingerprint, the read and the fallback together.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ACredentialOfTheApplicationThatHangs_StartsFromTheCacheWithinReadTimeout(bool honoursCancellation)
    {
        SeedCache("cached", credential: new HangingCredential(null));
        var builder = new ConfigurationBuilder();
        var engine = _fixture.Add(builder, options =>
        {
            options.CacheDirectory = EngineFixture.CacheDirectory;
            options.Credential = new HangingCredential(honoursCancellation);
            options.ReadTimeout = TimeSpan.FromMilliseconds(300);
            options.Resilience.AttemptTimeout = Timeout.InfiniteTimeSpan;
        });

        // On another thread, so a startup that hangs fails the test instead of hanging it.
        var root = _fixture.Track(await Task.Run(builder.Build).WaitAsync(EngineFixture.Patience));

        root["App:Name"].ShouldBe("cached");
        engine.Status.Origin.ShouldBe(StorytellerConfigurationOrigin.Cache);
        engine.Status.LastError.ShouldNotBeNull().Message.ShouldContain("ReadTimeout");
    }

    [Theory]
    [InlineData("""{ "format": 9999999999, "identity": "x", "storedAt": "2026-10-23T08:00:00Z", "document": {} }""")]
    [InlineData("""{ "format": 1e20, "identity": "x", "storedAt": "2026-10-23T08:00:00Z", "document": {} }""")]
    public void ACorruptCacheEntry_IsIgnored(string text)
    {
        var cache = new LastKnownGoodCache(EngineFixture.CacheDirectory, _fixture.FileSystem, null, _fixture.Time, NullLogger.Instance);
        var identity = DocumentIdentity.Create(new Uri(TestDocuments.Endpoint), FullKey.Parse(TestDocuments.Key), StorytellerReadMode.Resolved);
        _fixture.FileSystem.AddFile(cache.GetPath(new CacheIdentity(identity, CredentialFingerprint.Anonymous)), new MockFileData(text));
        _fixture.RespondDocument("one", EngineFixture.Tag(1));

        var (root, engine) = _fixture.Build(options => options.CacheDirectory = EngineFixture.CacheDirectory);

        root["App:Name"].ShouldBe("one");
        engine.Status.Origin.ShouldBe(StorytellerConfigurationOrigin.Platform);
    }

    [Fact]
    public void Load_UnderASingleThreadedSynchronizationContext_DoesNotDeadlock()
    {
        _fixture.Http.Respond(async (_, _) =>
        {
            await Task.Yield();
            return EngineFixture.Document("one", EngineFixture.Tag(1));
        });

        var builder = new ConfigurationBuilder();
        _fixture.Add(builder);
        var context = new UnpumpedSynchronizationContext();
        IConfigurationRoot? root = null;
        Exception? error = null;

        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);

            try
            {
                root = _fixture.Track(builder.Build());
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });

        thread.Start();

        thread.Join(EngineFixture.Patience).ShouldBeTrue();
        error.ShouldBeNull();
        root.ShouldNotBeNull()["App:Name"].ShouldBe("one");
    }

    [Fact]
    public async Task AFailedStartup_RunsAgainOnTheNextLoad()
    {
        _fixture.Http.Respond(HttpStatusCode.NotFound);
        _fixture.RespondDocument("one", EngineFixture.Tag(1));

        var builder = new ConfigurationBuilder();
        var engine = _fixture.Add(builder);

        Should.Throw<StorytellerConfigurationException>(() => _fixture.Track(builder.Build())).StatusCode.ShouldBe(404);
        (await engine.TryRefreshAsync()).ShouldBeFalse();

        var root = _fixture.Track(builder.Build());

        root["App:Name"].ShouldBe("one");
        _fixture.Http.Requests.Count.ShouldBe(2);
        engine.Status.IsDegraded.ShouldBeFalse();
        engine.Status.TotalFailures.ShouldBe(1);
    }

    [Fact]
    public void ALaterLoad_PublishesWithoutARequest()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, _) = _fixture.Build();

        root.Reload();

        root["App:Name"].ShouldBe("one");
        _fixture.Http.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public void UnboundBindings_AreWarnedAboutByPath_WithoutValues()
    {
        _fixture.Http.Respond(
            HttpStatusCode.OK,
            TestDocuments.Json("""{ "Db": { "Password": "@kv.secret-value-text" } }"""),
            ("ETag", EngineFixture.Tag(1)),
            ("Cache-Control", EngineFixture.PrivateNoCache));

        var (root, _) = _fixture.Build();

        root["Db:Password"].ShouldBe("@kv.secret-value-text");
        var warning = _fixture.Logs.Entries.Where(entry => entry.Level == LogLevel.Warning).ShouldHaveSingleItem();
        warning.Message.ShouldContain("$.Db.Password");
        warning.Message.ShouldNotContain("secret-value-text");
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }

    private void Arrange(Scenario scenario)
    {
        switch (scenario)
        {
            case Scenario.Unreachable or Scenario.UnreachableWithCache:
                _fixture.Http.Respond(HttpStatusCode.ServiceUnavailable);
                break;

            case Scenario.NotFound or Scenario.NotFoundWithCache:
                _fixture.Http.Respond(HttpStatusCode.NotFound);
                break;

            case Scenario.Unauthorized:
                _fixture.Http.Respond(HttpStatusCode.Unauthorized);
                break;

            case Scenario.Withheld:
                _fixture.Http.Respond(HttpStatusCode.OK, TestDocuments.Json(EngineFixture.Content("one")), ("X-2S-Secrets-Withheld", "mtls-required"));
                break;

            case Scenario.InvalidDocument:
                _fixture.Http.Respond(HttpStatusCode.OK, TestDocuments.Json("""{ "a:b": 1 }"""), ("ETag", EngineFixture.Tag(1)));
                break;
        }
    }

    // A first engine writes the entry, as an earlier run of the application would.
    private void SeedCache(string name, IStorytellerCacheProtector? protector = null, StorytellerCredential? credential = null)
    {
        _fixture.RespondDocument(name, EngineFixture.Tag(1));

        using var root = (IDisposable)new ConfigurationBuilder()
            .AddStoryteller(_fixture.Configure(options =>
            {
                options.CacheDirectory = EngineFixture.CacheDirectory;
                options.CacheProtector = protector;
                options.Credential = credential;
            }))
            .Build();
    }

    // A credential of the application whose token source hangs: until cancelled (true), for ever (false), or not at all
    // (null). Every instance has the same fingerprint.
    private sealed class HangingCredential : StorytellerCredential
    {
        private readonly bool? _honoursCancellation;

        public HangingCredential(bool? honoursCancellation)
        {
            _honoursCancellation = honoursCancellation;
        }

        public override async ValueTask<AuthenticationHeaderValue?> GetAuthorizationAsync(AuthenticationHeaderValue? rejected, CancellationToken cancellationToken)
        {
            switch (_honoursCancellation)
            {
                case true:
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    break;

                case false:
                    await new TaskCompletionSource().Task;
                    break;
            }

            return new AuthenticationHeaderValue("Bearer", "token");
        }
    }

    // Queues what is posted to it and never runs it: a continuation that needs it never comes.
    private sealed class UnpumpedSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            throw new NotSupportedException();
        }
    }
}
