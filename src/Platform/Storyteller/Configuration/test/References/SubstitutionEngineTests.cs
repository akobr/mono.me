using System.Net;
using _42.Platform.Storyteller.Sdk.Configuring.Engine;
using _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Engine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.References;

// Substitution in the provider: the references of a document at startup, on a refresh, on a 304 and on a switch.
public sealed class SubstitutionEngineTests : IDisposable
{
    private const string Secret = "S3cr3t-Value-0f-The-Vault";
    private const string Reference = "kv://my-vault/db-password";
    private const string Content = """{ "Db": { "Host": "db.local", "Password": "kv://my-vault/db-password", "Replicas": [ "aws-sm://replica", "plain" ] } }""";

    private readonly EngineFixture _fixture = new();
    private readonly FakeResolver _resolver = new()
    {
        Secrets =
        {
            [Reference] = Secret,
            ["aws-sm://replica"] = "replica-secret",
        },
    };

    public enum Scenario
    {
        ResolverTransient,
        ResolverDefinitive,
        ResolverHangs,
        UnreachableWithCacheThatResolves,
        UnreachableWithCacheThatFailsTransiently,
        UnreachableWithCacheThatFailsDefinitively,
        ConfirmedCacheThatFailsTransiently,
        ConfirmedCacheThatFailsDefinitively,
    }

    // The resolver rows of the startup table of section 11, and cache entries whose references cannot be resolved.
    // null: Load() throws.
    public static TheoryData<Scenario, bool, StorytellerConfigurationOrigin?, bool> Table()
    {
        return new TheoryData<Scenario, bool, StorytellerConfigurationOrigin?, bool>
        {
            { Scenario.ResolverTransient, false, null, true },
            { Scenario.ResolverTransient, true, StorytellerConfigurationOrigin.Empty, true },
            { Scenario.ResolverDefinitive, false, null, false },
            { Scenario.ResolverDefinitive, true, null, false },
            { Scenario.ResolverHangs, false, null, true },
            { Scenario.ResolverHangs, true, StorytellerConfigurationOrigin.Empty, true },
            { Scenario.UnreachableWithCacheThatResolves, false, StorytellerConfigurationOrigin.Cache, true },
            { Scenario.UnreachableWithCacheThatResolves, true, StorytellerConfigurationOrigin.Cache, true },
            { Scenario.UnreachableWithCacheThatFailsTransiently, false, null, true },
            { Scenario.UnreachableWithCacheThatFailsTransiently, true, StorytellerConfigurationOrigin.Empty, true },
            { Scenario.UnreachableWithCacheThatFailsDefinitively, false, null, true },
            { Scenario.UnreachableWithCacheThatFailsDefinitively, true, StorytellerConfigurationOrigin.Empty, true },
            { Scenario.ConfirmedCacheThatFailsTransiently, false, null, true },
            { Scenario.ConfirmedCacheThatFailsTransiently, true, StorytellerConfigurationOrigin.Empty, true },
            { Scenario.ConfirmedCacheThatFailsDefinitively, false, null, false },
            { Scenario.ConfirmedCacheThatFailsDefinitively, true, null, false },
        };
    }

    [Theory]
    [MemberData(nameof(Table))]
    public void TheResolverRowsOfTheStartupTable(Scenario scenario, bool optional, StorytellerConfigurationOrigin? origin, bool isTransient)
    {
        var withCache = scenario is not (Scenario.ResolverTransient or Scenario.ResolverDefinitive or Scenario.ResolverHangs);
        var protector = scenario is Scenario.ConfirmedCacheThatFailsTransiently or Scenario.ConfirmedCacheThatFailsDefinitively ? new TestCacheProtector() : null;

        if (withCache)
        {
            SeedCache(protector);
        }

        Arrange(scenario);

        var builder = new ConfigurationBuilder();
        var engine = _fixture.Add(builder, options =>
        {
            options.Optional = optional;
            options.ReferenceResolver = _resolver;
            options.CacheDirectory = withCache ? EngineFixture.CacheDirectory : null;
            options.CacheProtector = protector;
            options.ReadTimeout = scenario == Scenario.ResolverHangs ? TimeSpan.FromMilliseconds(300) : options.ReadTimeout;
        });

        if (origin is null)
        {
            var exception = Should.Throw<StorytellerConfigurationException>(() => _fixture.Track(builder.Build()));
            exception.IsTransient.ShouldBe(isTransient);
            exception.Key.ShouldBe(TestDocuments.Key);
            engine.Status.LastError.ShouldBeSameAs(exception);
            exception.ToString().ShouldNotContain(Secret);
            return;
        }

        var root = _fixture.Track(builder.Build());
        var status = engine.Status;

        status.Origin.ShouldBe(origin.Value);
        status.IsDegraded.ShouldBeTrue();
        status.LastError.ShouldNotBeNull().IsTransient.ShouldBe(isTransient);
        status.ConsecutiveFailures.ShouldBe(1);

        // Never the text of a reference in place of its secret.
        root["Db:Password"].ShouldBe(origin == StorytellerConfigurationOrigin.Cache ? Secret : null);
    }

    [Fact]
    public void References_AreResolvedOnApply_AndTheCacheKeepsTheServersDocument()
    {
        RespondContent(Content, EngineFixture.Tag(1));

        var (root, engine) = _fixture.Build(options =>
        {
            options.ReferenceResolver = _resolver;
            options.CacheDirectory = EngineFixture.CacheDirectory;
        });

        root["Db:Host"].ShouldBe("db.local");
        root["Db:Password"].ShouldBe(Secret);
        root["Db:Replicas:0"].ShouldBe("replica-secret");
        root["Db:Replicas:1"].ShouldBe("plain");
        engine.Status.IsDegraded.ShouldBeFalse();

        var file = _fixture.FileSystem.Directory.GetFiles(EngineFixture.CacheDirectory).ShouldHaveSingleItem();
        var text = _fixture.FileSystem.File.ReadAllText(file);
        text.ShouldContain(Reference);
        text.ShouldNotContain(Secret);
        _fixture.Logs.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public void TheEffectiveMode_ResolvesNothing()
    {
        RespondContent(Content, "W/\"e.AAECAwQFBgcICQoLDA0ODw\"");

        var (root, _) = _fixture.Build(options =>
        {
            options.Mode = StorytellerReadMode.Effective;
            options.ReferenceResolver = _resolver;
        });

        root["Db:Password"].ShouldBe(Reference);
        _resolver.Asked.ShouldBeEmpty();
        _fixture.Logs.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task WithoutAResolver_ReferencesStayAsWritten_WithOneWarningPerAppliedDocument()
    {
        RespondContent(
            """{ "Db": { "Password": "kv://my-vault/db-password", "Uri": "https://vault.vault.azure.net/secrets/name", "Plain": "aws-sm://replica" } }""",
            EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build(options => options.RefreshInterval = TimeSpan.Zero);

        root["Db:Password"].ShouldBe(Reference);
        root["Db:Uri"].ShouldBe("https://vault.vault.azure.net/secrets/name");
        var warning = ReferenceWarnings().ShouldHaveSingleItem();
        warning.Message.ShouldContain("$.Db.Password, $.Db.Uri");
        warning.Message.ShouldNotContain("$.Db.Plain");

        // A 304 applies nothing new; a new document is warned about once more.
        _fixture.RespondNotModified(EngineFixture.Tag(1));
        (await engine.TryRefreshAsync()).ShouldBeTrue();
        ReferenceWarnings().Count.ShouldBe(1);

        RespondContent("""{ "Db": { "Password": "kv://my-vault/other" } }""", EngineFixture.Tag(2));
        (await engine.TryRefreshAsync()).ShouldBeTrue();
        ReferenceWarnings().Count.ShouldBe(2);
        ReferenceWarnings()[^1].Message.ShouldContain("$.Db.Password");
    }

    [Fact]
    public void AnUnclaimedShorthand_FailsTheStartup_AlsoOfAnOptionalProvider()
    {
        RespondContent(Content, EngineFixture.Tag(1));
        _resolver.Accepts = value => value.StartsWith("aws-sm://", StringComparison.Ordinal);
        var builder = new ConfigurationBuilder();
        _fixture.Add(builder, options =>
        {
            options.Optional = true;
            options.ReferenceResolver = _resolver;
        });

        var exception = Should.Throw<StorytellerConfigurationException>(() => _fixture.Track(builder.Build()));

        exception.IsTransient.ShouldBeFalse();
        exception.Message.ShouldContain("$.Db.Password");
        exception.Message.ShouldContain("my-vault");
        exception.Message.ShouldContain("db-password");
    }

    [Fact]
    public void AnUnclaimedSecretUri_StaysAsWritten_WithAWarningNamingItsPathAndHost()
    {
        RespondContent("""{ "Db": { "Password": "kv://my-vault/db-password", "Other": "https://other.vault.example/secrets/x" } }""", EngineFixture.Tag(1));

        var (root, _) = _fixture.Build(options => options.ReferenceResolver = _resolver);

        root["Db:Password"].ShouldBe(Secret);
        root["Db:Other"].ShouldBe("https://other.vault.example/secrets/x");
        var warning = ReferenceWarnings().ShouldHaveSingleItem();
        warning.Message.ShouldContain("$.Db.Other (other.vault.example)");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AFailedResolutionOnARefresh_KeepsThePreviousData_WithItsClass(bool isTransient)
    {
        RespondContent(Content, EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build(options =>
        {
            options.ReferenceResolver = _resolver;
            options.RefreshInterval = TimeSpan.Zero;
        });
        var changes = CountChanges(root);
        _resolver.Accepts = value => value.Contains("://", StringComparison.Ordinal);
        _resolver.Respond = (value, _) => value == "kv://my-vault/api-key"
            ? throw new SecretResolutionException("The vault refused the read.", isTransient, "my-vault", "api-key")
            : Task.FromResult(_resolver.Secrets[value]);
        RespondContent("""{ "Db": { "Password": "kv://my-vault/db-password", "ApiKey": "kv://my-vault/api-key" } }""", EngineFixture.Tag(2));

        (await engine.TryRefreshAsync()).ShouldBeFalse();

        changes().ShouldBe(0);
        root["Db:Password"].ShouldBe(Secret);
        root["Db:ApiKey"].ShouldBeNull();
        var status = engine.Status;
        status.ETag.ShouldBe(EngineFixture.Tag(1));
        status.IsDegraded.ShouldBeTrue();
        var error = status.LastError.ShouldNotBeNull();
        error.IsTransient.ShouldBe(isTransient);
        error.Message.ShouldContain("$.Db.ApiKey");
        error.Message.ShouldContain("The vault refused the read.");
        error.InnerException.ShouldBeOfType<SecretResolutionException>().SecretName.ShouldBe("api-key");

        // The next poll asks for the document again.
        RespondContent("""{ "Db": { "Password": "kv://my-vault/db-password", "ApiKey": "kv://my-vault/api-key" } }""", EngineFixture.Tag(2));
        (await engine.TryRefreshAsync()).ShouldBeFalse();
        _fixture.Http.Requests[^1].Headers["If-None-Match"].ShouldBe(EngineFixture.Tag(1));
    }

    [Fact]
    public async Task AChangedDocument_ReusesFreshValues()
    {
        RespondContent(Content, EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build(options =>
        {
            options.ReferenceResolver = _resolver;
            options.RefreshInterval = TimeSpan.Zero;
        });
        _fixture.Time.Advance(TimeSpan.FromMinutes(10));
        RespondContent("""{ "Db": { "Name": "two", "Password": "kv://my-vault/db-password" } }""", EngineFixture.Tag(2));

        (await engine.TryRefreshAsync()).ShouldBeTrue();

        root["Db:Name"].ShouldBe("two");
        root["Db:Password"].ShouldBe(Secret);
        _resolver.Resolved.ShouldBe([Reference, "aws-sm://replica"]);
    }

    [Fact]
    public async Task OnA304_ExpiredReferencesAreResolvedAgain_AndOnlyAChangedValueReloads()
    {
        RespondContent(Content, EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build(options =>
        {
            options.ReferenceResolver = _resolver;
            options.RefreshInterval = TimeSpan.Zero;
            options.SecretRefreshInterval = TimeSpan.FromHours(1);
        });
        var changes = CountChanges(root);
        _resolver.Resolved.Count.ShouldBe(2);

        // Within the interval, a 304 resolves nothing.
        _fixture.Time.Advance(TimeSpan.FromMinutes(30));
        _fixture.RespondNotModified(EngineFixture.Tag(1));
        (await engine.TryRefreshAsync()).ShouldBeTrue();
        _resolver.Resolved.Count.ShouldBe(2);

        // After it, the values are resolved again; the same values reload nothing.
        _fixture.Time.Advance(TimeSpan.FromMinutes(30));
        _fixture.RespondNotModified(EngineFixture.Tag(1));
        (await engine.TryRefreshAsync()).ShouldBeTrue();
        _resolver.Resolved.Count.ShouldBe(4);
        changes().ShouldBe(0);

        // A rotated secret reaches the configuration without a change of the document, with one reload.
        _resolver.Secrets[Reference] = "rotated";
        _fixture.Time.Advance(TimeSpan.FromHours(1));
        _fixture.RespondNotModified(EngineFixture.Tag(1));
        (await engine.TryRefreshAsync()).ShouldBeTrue();

        _resolver.Resolved.Count.ShouldBe(6);
        changes().ShouldBe(1);
        root["Db:Password"].ShouldBe("rotated");
        var status = engine.Status;
        status.ETag.ShouldBe(EngineFixture.Tag(1));
        status.IsDegraded.ShouldBeFalse();
        status.LastAppliedAt.ShouldBe(_fixture.Time.GetUtcNow());
        _fixture.Http.Requests.Select(request => request.Headers.GetValueOrDefault("If-None-Match")).ShouldBe(new string?[] { null, EngineFixture.Tag(1), EngineFixture.Tag(1), EngineFixture.Tag(1) });
    }

    [Fact]
    public async Task ARotatedSecretThatDoesNotBind_IsRolledBackOnce_AndAppliedWhenItRotatesAgain()
    {
        RespondContent("""{ "Db": { "Password": "kv://my-vault/db-password" } }""", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build(options =>
        {
            options.ReferenceResolver = _resolver;
            options.RefreshInterval = TimeSpan.Zero;
            options.SecretRefreshInterval = TimeSpan.FromHours(1);
        });
        var services = new ServiceCollection();
        services.AddOptions<DbOptions>().Bind(root.GetSection("Db")).Validate(options => options.Password != "bad-secret");
        using var container = services.BuildServiceProvider();
        var monitor = container.GetRequiredService<IOptionsMonitor<DbOptions>>();
        monitor.CurrentValue.Password.ShouldBe(Secret);
        var changes = CountChanges(root);

        // The vault returns a value that does not bind: it is applied, and rolled back.
        _resolver.Secrets[Reference] = "bad-secret";
        _fixture.Time.Advance(TimeSpan.FromHours(1));
        _fixture.RespondNotModified(EngineFixture.Tag(1));
        (await engine.TryRefreshAsync()).ShouldBeFalse();

        changes().ShouldBe(2);
        monitor.CurrentValue.Password.ShouldBe(Secret);
        _resolver.Resolved.Count.ShouldBe(2);
        var status = engine.Status;
        status.TotalFailures.ShouldBe(1);
        status.IsDegraded.ShouldBeTrue();
        status.LastError.ShouldNotBeNull().Message.ShouldContain("resolved again");

        // Polls of the unchanged document apply nothing: the memory cache answers until the value expires, then the
        // resolver, which still returns it.
        for (var poll = 0; poll < 10; poll++)
        {
            _fixture.Time.Advance(TimeSpan.FromSeconds(30));
            _fixture.RespondNotModified(EngineFixture.Tag(1));
            (await engine.TryRefreshAsync()).ShouldBeTrue();
        }

        _resolver.Resolved.Count.ShouldBe(2);
        _fixture.Time.Advance(TimeSpan.FromHours(1));
        _fixture.RespondNotModified(EngineFixture.Tag(1));
        (await engine.TryRefreshAsync()).ShouldBeTrue();

        changes().ShouldBe(2);
        _resolver.Resolved.Count.ShouldBe(3);
        monitor.CurrentValue.Password.ShouldBe(Secret);
        status = engine.Status;
        status.TotalFailures.ShouldBe(1);
        status.ConsecutiveFailures.ShouldBe(0);
        status.IsDegraded.ShouldBeTrue();

        // A value rotated again is applied.
        _resolver.Secrets[Reference] = "rotated";
        _fixture.Time.Advance(TimeSpan.FromHours(1));
        _fixture.RespondNotModified(EngineFixture.Tag(1));
        (await engine.TryRefreshAsync()).ShouldBeTrue();

        changes().ShouldBe(3);
        monitor.CurrentValue.Password.ShouldBe("rotated");
        engine.Status.IsDegraded.ShouldBeFalse();
        engine.Status.TotalFailures.ShouldBe(1);
        _fixture.Http.Requests.Skip(1).ShouldAllBe(request => request.Headers["If-None-Match"] == EngineFixture.Tag(1));
    }

    [Fact]
    public async Task AFailedResolutionOnA304_KeepsTheResolvedValues_AndIsTriedAgain()
    {
        RespondContent(Content, EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build(options =>
        {
            options.ReferenceResolver = _resolver;
            options.RefreshInterval = TimeSpan.Zero;
        });
        _fixture.Time.Advance(TimeSpan.FromHours(1));
        _resolver.Respond = (_, _) => throw new SecretResolutionException("The vault is busy.", true, "my-vault", "db-password");
        _fixture.RespondNotModified(EngineFixture.Tag(1));

        (await engine.TryRefreshAsync()).ShouldBeFalse();

        root["Db:Password"].ShouldBe(Secret);
        var status = engine.Status;
        status.LastError.ShouldNotBeNull().IsTransient.ShouldBeTrue();
        status.LastSucceededAt.ShouldBe(_fixture.Time.GetUtcNow());

        _resolver.Respond = null;
        _resolver.Secrets[Reference] = "rotated";
        _fixture.RespondNotModified(EngineFixture.Tag(1));

        (await engine.TryRefreshAsync()).ShouldBeTrue();

        root["Db:Password"].ShouldBe("rotated");
        engine.Status.IsDegraded.ShouldBeFalse();
    }

    [Fact]
    public async Task ASwitch_ResolvesTheReferencesOfTheNewDocument_AndFailsWhenTheyDoNotResolve()
    {
        RespondContent(Content, EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build(options =>
        {
            options.ReferenceResolver = _resolver;
            options.RefreshInterval = TimeSpan.Zero;
        });
        _resolver.Secrets["kv://my-vault/contoso"] = "contoso-secret";
        RespondContent("""{ "Db": { "Password": "kv://my-vault/contoso" } }""", EngineFixture.Tag(2));

        await engine.SwitchKeyAsync("sbt.contoso");

        engine.ActiveKey.ShouldBe("@acme.billing.default.sbt.contoso");
        root["Db:Password"].ShouldBe("contoso-secret");

        RespondContent("""{ "Db": { "Password": "kv://my-vault/fabrikam" } }""", EngineFixture.Tag(3));
        _resolver.Accepts = value => value.StartsWith("kv://", StringComparison.Ordinal);
        _resolver.Respond = (value, _) => _resolver.Secrets.TryGetValue(value, out var secret)
            ? Task.FromResult(secret)
            : throw new SecretResolutionException("Not found.", false, "my-vault", "fabrikam");

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => engine.SwitchKeyAsync("sbt.fabrikam"));

        exception.IsTransient.ShouldBeFalse();
        exception.Message.ShouldContain("$.Db.Password");
        engine.ActiveKey.ShouldBe("@acme.billing.default.sbt.contoso");
        root["Db:Password"].ShouldBe("contoso-secret");
    }

    [Fact]
    public async Task NoResolvedValue_IsLogged_WrittenToDisk_OrPutInAnExceptionOrTheStatus()
    {
        var exceptions = new List<Exception>();
        _resolver.Secrets["kv://my-vault/port"] = $"port {Secret}";
        RespondContent("""{ "Db": { "Password": "kv://my-vault/db-password", "Port": 5432 } }""", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build(options =>
        {
            options.ReferenceResolver = _resolver;
            options.RefreshInterval = TimeSpan.Zero;
            options.CacheDirectory = EngineFixture.CacheDirectory;
        });
        var services = new ServiceCollection();
        services.AddOptions<DbOptions>().Bind(root.GetSection("Db"));
        using var container = services.BuildServiceProvider();
        var monitor = container.GetRequiredService<IOptionsMonitor<DbOptions>>();
        monitor.CurrentValue.Password.ShouldBe(Secret);

        // A resolved value that does not bind: the binder may quote it, the provider does not.
        RespondContent("""{ "Db": { "Password": "kv://my-vault/db-password", "Port": "kv://my-vault/port" } }""", EngineFixture.Tag(2));
        (await engine.TryRefreshAsync()).ShouldBeFalse();
        monitor.CurrentValue.Port.ShouldBe(5432);
        engine.Status.LastError.ShouldNotBeNull().Message.ShouldContain("did not bind");
        exceptions.Add(engine.Status.LastError!);

        // A resolution that fails, in a document whose other values resolve.
        _resolver.Accepts = value => value.StartsWith("kv://", StringComparison.Ordinal);
        _resolver.Respond = (value, _) => _resolver.Secrets.TryGetValue(value, out var secret)
            ? Task.FromResult(secret)
            : throw new SecretResolutionException("The secret is disabled.", false, "my-vault", "missing");
        RespondContent("""{ "Db": { "Password": "kv://my-vault/db-password", "Other": "kv://my-vault/missing" } }""", EngineFixture.Tag(3));
        (await engine.TryRefreshAsync()).ShouldBeFalse();
        exceptions.Add(engine.Status.LastError!);

        // A switch that fails, and a startup that fails.
        RespondContent("""{ "Db": { "Password": "kv://my-vault/db-password", "Other": "kv://my-vault/missing" } }""", EngineFixture.Tag(4));
        exceptions.Add(await Should.ThrowAsync<StorytellerConfigurationException>(() => engine.SwitchKeyAsync("sbt.contoso")));

        RespondContent("""{ "Db": { "Password": "kv://my-vault/db-password", "Other": "kv://my-vault/missing" } }""", EngineFixture.Tag(5));
        var builder = new ConfigurationBuilder();
        _fixture.Add(builder, options => options.ReferenceResolver = _resolver);
        exceptions.Add(Should.Throw<StorytellerConfigurationException>(() => _fixture.Track(builder.Build())));

        root["Db:Password"].ShouldBe(Secret);
        _fixture.Logs.Entries.ShouldNotBeEmpty();

        foreach (var entry in _fixture.Logs.Entries)
        {
            entry.Message.ShouldNotContain(Secret);
            (entry.Exception?.ToString() ?? string.Empty).ShouldNotContain(Secret);
        }

        foreach (var exception in exceptions)
        {
            exception.ToString().ShouldNotContain(Secret);
        }

        engine.Status.ToString().ShouldNotContain(Secret);
        engine.Snapshot.ToString().ShouldNotContain(Secret);
        engine.Snapshot.Substitution.ShouldNotBeNull().ToString().ShouldNotContain(Secret);

        foreach (var file in _fixture.FileSystem.AllFiles)
        {
            _fixture.FileSystem.File.ReadAllText(file).ShouldNotContain(Secret);
        }
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }

    private void Arrange(Scenario scenario)
    {
        switch (scenario)
        {
            case Scenario.ResolverTransient or Scenario.ResolverDefinitive:
                RespondContent(Content, EngineFixture.Tag(1));
                Fail(scenario == Scenario.ResolverTransient);
                break;

            case Scenario.ResolverHangs:
                RespondContent(Content, EngineFixture.Tag(1));

                // Ignores cancellation: the ReadTimeout of the startup ends it.
                _resolver.Respond = (_, _) => new TaskCompletionSource<string>().Task;
                break;

            case Scenario.UnreachableWithCacheThatResolves:
                _fixture.Http.Respond(HttpStatusCode.ServiceUnavailable);
                break;

            case Scenario.UnreachableWithCacheThatFailsTransiently or Scenario.UnreachableWithCacheThatFailsDefinitively:
                _fixture.Http.Respond(HttpStatusCode.ServiceUnavailable);
                Fail(scenario == Scenario.UnreachableWithCacheThatFailsTransiently);
                break;

            case Scenario.ConfirmedCacheThatFailsTransiently or Scenario.ConfirmedCacheThatFailsDefinitively:
                // The platform confirms the protected entry; its references do not resolve.
                _fixture.RespondNotModified(EngineFixture.Tag(1));
                Fail(scenario == Scenario.ConfirmedCacheThatFailsTransiently);
                break;
        }
    }

    private void Fail(bool isTransient)
    {
        _resolver.Respond = (_, _) => throw new SecretResolutionException("The vault cannot be read.", isTransient, "my-vault", "db-password");
    }

    // A first engine, with a resolver that works, writes the entry, as an earlier run of the application would.
    private void SeedCache(IStorytellerCacheProtector? protector)
    {
        RespondContent(Content, EngineFixture.Tag(1));

        using var root = (IDisposable)new ConfigurationBuilder()
            .AddStoryteller(_fixture.Configure(options =>
            {
                options.CacheDirectory = EngineFixture.CacheDirectory;
                options.CacheProtector = protector;
                options.ReferenceResolver = _resolver;
            }))
            .Build();
    }

    private void RespondContent(string content, string etag)
    {
        _fixture.Http.Respond(HttpStatusCode.OK, TestDocuments.Json(content), ("ETag", etag), ("Cache-Control", EngineFixture.PrivateNoCache));
    }

    private List<RecordingLoggerFactory.LogEntry> ReferenceWarnings()
    {
        return [.. _fixture.Logs.Entries.Where(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("secret references", StringComparison.Ordinal))];
    }

    private Func<int> CountChanges(IConfigurationRoot root)
    {
        var count = 0;
        _fixture.Track(ChangeToken.OnChange(root.GetReloadToken, () => Interlocked.Increment(ref count)));
        return () => Volatile.Read(ref count);
    }

    public sealed class DbOptions
    {
        public string? Password { get; set; }

        public int Port { get; set; }
    }
}
