using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Engine;

public sealed class SwitchingTests : IDisposable
{
    private const string OtherKey = "@acme.billing.default.sbt.contoso";
    private const string OtherPath = "/api/v1/acme/billing/default/configuration/sbt.contoso/resolved";
    private const string ActivePath = "/api/v1/acme/billing/default/configuration/sbt.northwind/resolved";

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly EngineFixture _fixture = new();

    [Fact]
    public async Task ASwitch_AppliesTheNewDocument_AndTheActiveKey_VisibleTogetherInAChangeCallback()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build();
        var seen = new List<(string Key, string? Name)>();
        _fixture.Track(ChangeToken.OnChange(root.GetReloadToken, () => seen.Add((engine.ActiveKey, root["App:Name"]))));
        _fixture.RespondDocument("contoso", EngineFixture.Tag(2), version: 7);

        await engine.SwitchKeyAsync(OtherKey);

        var request = _fixture.Http.Requests[^1];
        request.Uri.AbsolutePath.ShouldBe(OtherPath);
        request.Headers.ContainsKey("If-None-Match").ShouldBeFalse();
        seen.ShouldBe([(OtherKey, "contoso")]);
        engine.ActiveKey.ShouldBe(OtherKey);
        engine.Status.ETag.ShouldBe(EngineFixture.Tag(2));
        root["App:Name"].ShouldBe("contoso");

        // The poller follows the new key.
        _fixture.RespondNotModified(EngineFixture.Tag(2));
        await _fixture.AdvanceAsync(engine, Interval);

        _fixture.Http.Requests[^1].Uri.AbsolutePath.ShouldBe(OtherPath);
        _fixture.Http.Requests[^1].Headers["If-None-Match"].ShouldBe(EngineFixture.Tag(2));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    public async Task AFailedSwitch_KeepsTheKeyAndTheDocument(HttpStatusCode status, bool isTransient)
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build();
        var changes = 0;
        _fixture.Track(ChangeToken.OnChange(root.GetReloadToken, () => changes++));
        _fixture.Http.Respond(status);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => engine.SwitchKeyAsync(OtherKey));

        exception.Key.ShouldBe(OtherKey);
        exception.IsTransient.ShouldBe(isTransient);
        engine.ActiveKey.ShouldBe(TestDocuments.Key);
        root["App:Name"].ShouldBe("one");
        changes.ShouldBe(0);
        engine.Status.LastError.ShouldBeSameAs(exception);
        engine.Status.ConsecutiveFailures.ShouldBe(1);

        _fixture.RespondNotModified(EngineFixture.Tag(1));
        await _fixture.AdvanceAsync(engine, Interval);

        _fixture.Http.Requests[^1].Uri.AbsolutePath.ShouldBe(ActivePath);
        engine.Status.IsDegraded.ShouldBeFalse();
    }

    [Fact]
    public async Task AnAnnotationKey_ResolvesInTheActiveOrganizationProjectAndView()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build();
        _fixture.Http.Otherwise = (_, _) => Task.FromResult(EngineFixture.Document("other", EngineFixture.Tag(2)));

        await engine.SwitchKeyAsync("sbt.contoso");

        _fixture.Http.Requests[^1].Uri.AbsolutePath.ShouldBe(OtherPath);
        engine.ActiveKey.ShouldBe(OtherKey);

        await engine.SwitchKeyAsync("@acme.billing.staging.sbt.contoso");
        await engine.SwitchKeyAsync(" cnt.fabrikam.eu ");

        _fixture.Http.Requests[^1].Uri.AbsolutePath.ShouldBe("/api/v1/acme/billing/staging/configuration/cnt.fabrikam.eu/resolved");
        engine.ActiveKey.ShouldBe("@acme.billing.staging.cnt.fabrikam.eu");
        root["App:Name"].ShouldBe("other");
    }

    [Theory]
    [InlineData("@globex.billing.default.sbt.contoso")]
    [InlineData("@acme.shipping.default.sbt.contoso")]
    [InlineData("@acme.billing.default")]
    [InlineData("@acme.billing.default.xyz.contoso")]
    [InlineData("not a key")]
    [InlineData(" ")]
    public async Task AKeyOutsideTheProject_OrNotAKey_IsRejected(string key)
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (_, engine) = _fixture.Build();

        await Should.ThrowAsync<ArgumentException>(() => engine.SwitchKeyAsync(key));

        _fixture.Http.Requests.Count.ShouldBe(1);
        engine.ActiveKey.ShouldBe(TestDocuments.Key);
        engine.Status.ConsecutiveFailures.ShouldBe(0);
    }

    [Fact]
    public async Task ASwitch_WaitsForARunningPoll()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build();
        var gate = new GatedResponse(() => EngineFixture.Document("two", EngineFixture.Tag(2), version: 4));
        _fixture.Http.Respond(gate.RespondAsync);
        _fixture.RespondDocument("contoso", EngineFixture.Tag(3));

        var refresh = engine.TryRefreshAsync();
        await gate.Entered;
        var switching = engine.SwitchKeyAsync(OtherKey);

        // The poll holds the run gate; the switch neither cancels it nor sends its request.
        switching.IsCompleted.ShouldBeFalse();
        _fixture.Http.Requests.Count.ShouldBe(2);
        gate.Release();

        (await refresh.WaitAsync(EngineFixture.Patience)).ShouldBeTrue();
        await switching.WaitAsync(EngineFixture.Patience);

        _fixture.Http.Requests.Select(request => request.Uri.AbsolutePath).ShouldBe([ActivePath, ActivePath, OtherPath]);
        engine.ActiveKey.ShouldBe(OtherKey);
        root["App:Name"].ShouldBe("contoso");
    }

    // While a slow listener holds the notification of "two", the switch swaps in its document and waits. A refresh then
    // swaps in a newer document of the new key that does not bind; one pass covers both and rolls back to the old key.
    [Fact]
    public async Task ASwitchWhoseSnapshotARejectedNewerOneReplaced_Throws_AndKeepsThePreviousKey()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build();
        var services = new ServiceCollection();
        services.AddOptions<RefreshTests.AppOptions>().Bind(root.GetSection("App")).ValidateDataAnnotations();
        using var container = services.BuildServiceProvider();
        var monitor = container.GetRequiredService<IOptionsMonitor<RefreshTests.AppOptions>>();
        monitor.CurrentValue.Port.ShouldBe(80);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var listener = monitor.OnChange(options =>
        {
            if (options.Name == "two" && entered.TrySetResult())
            {
                release.Task.Wait(EngineFixture.Patience);
            }
        });

        _fixture.RespondDocument("two", EngineFixture.Tag(2), version: 4, port: 81);
        _fixture.RespondDocument("contoso", EngineFixture.Tag(3), version: 7, port: 82);
        _fixture.RespondDocument("contoso-bad", EngineFixture.Tag(4), version: 8, port: 0);

        var first = engine.TryRefreshAsync();
        await entered.Task.WaitAsync(EngineFixture.Patience);
        var switching = engine.SwitchKeyAsync(OtherKey);
        SpinWait.SpinUntil(() => engine.Snapshot.ETag == EngineFixture.Tag(3), EngineFixture.Patience).ShouldBeTrue();
        var second = engine.TryRefreshAsync();
        SpinWait.SpinUntil(() => engine.Snapshot.ETag == EngineFixture.Tag(4), EngineFixture.Patience).ShouldBeTrue();
        release.SetResult();

        (await first.WaitAsync(EngineFixture.Patience)).ShouldBeTrue();
        (await second.WaitAsync(EngineFixture.Patience)).ShouldBeFalse();
        await Should.ThrowAsync<StorytellerConfigurationException>(() => switching.WaitAsync(EngineFixture.Patience));

        engine.ActiveKey.ShouldNotBe(OtherKey);
        root["App:Name"].ShouldBe("two");
        monitor.CurrentValue.Port.ShouldBe(81);
    }

    [Fact]
    public async Task SwitchingAndRefreshing_FromAnOptionsChangeCallback_DoNotDeadlock()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build();
        var services = new ServiceCollection();
        services.AddOptions<RefreshTests.AppOptions>().Bind(root.GetSection("App"));
        using var container = services.BuildServiceProvider();
        var monitor = container.GetRequiredService<IOptionsMonitor<RefreshTests.AppOptions>>();
        var isDone = false;

        using var listener = monitor.OnChange(options =>
        {
            if (options.Name == "one-b" && !isDone)
            {
                isDone = true;
                engine.SwitchKeyAsync(OtherKey).GetAwaiter().GetResult();
                engine.TryRefreshAsync().GetAwaiter().GetResult().ShouldBeTrue();
            }
        });

        _fixture.RespondDocument("one-b", EngineFixture.Tag(2), version: 4);
        _fixture.RespondDocument("contoso", EngineFixture.Tag(3));
        _fixture.RespondNotModified(EngineFixture.Tag(3));

        (await engine.TryRefreshAsync().WaitAsync(EngineFixture.Patience)).ShouldBeTrue();

        isDone.ShouldBeTrue();
        engine.ActiveKey.ShouldBe(OtherKey);
        monitor.CurrentValue.Name.ShouldBe("contoso");
        _fixture.Http.Requests.Count.ShouldBe(4);
        _fixture.Http.Requests[^1].Headers["If-None-Match"].ShouldBe(EngineFixture.Tag(3));
    }

    [Fact]
    public async Task ARejectedSwitch_Throws_AndRestoresThePreviousKey()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build();
        _fixture.Track(ChangeToken.OnChange(root.GetReloadToken, () =>
        {
            if (root["App:Name"] == "contoso")
            {
                throw new FormatException("The document does not bind.");
            }
        }));
        _fixture.RespondDocument("contoso", EngineFixture.Tag(2));

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => engine.SwitchKeyAsync(OtherKey));

        exception.Message.ShouldContain("did not bind");
        exception.Message.ShouldContain(nameof(FormatException));
        exception.Key.ShouldBe(OtherKey);
        engine.ActiveKey.ShouldBe(TestDocuments.Key);
        root["App:Name"].ShouldBe("one");
        engine.Snapshot.ETag.ShouldBe(EngineFixture.Tag(1));
        engine.Snapshot.Rejection.ShouldBeNull();

        _fixture.RespondNotModified(EngineFixture.Tag(1));
        await _fixture.AdvanceAsync(engine, Interval);

        _fixture.Http.Requests[^1].Uri.AbsolutePath.ShouldBe(ActivePath);
        _fixture.Http.Requests[^1].Headers["If-None-Match"].ShouldBe(EngineFixture.Tag(1));
        engine.Status.IsDegraded.ShouldBeFalse();
    }

    [Fact]
    public async Task ASwitchToTheActiveKey_IsARefreshThatThrows()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (_, engine) = _fixture.Build();
        _fixture.RespondNotModified(EngineFixture.Tag(1));
        _fixture.Http.Respond(HttpStatusCode.ServiceUnavailable);

        await engine.SwitchKeyAsync("sbt.northwind");

        _fixture.Http.Requests[^1].Headers["If-None-Match"].ShouldBe(EngineFixture.Tag(1));

        (await Should.ThrowAsync<StorytellerConfigurationException>(() => engine.SwitchKeyAsync(TestDocuments.Key))).IsTransient.ShouldBeTrue();
        engine.ActiveKey.ShouldBe(TestDocuments.Key);
    }

    [Fact]
    public async Task BeforeTheFirstLoad_ASwitchThrows_AndARefreshReturnsFalse()
    {
        var builder = new ConfigurationBuilder();
        var engine = _fixture.Add(builder);

        await Should.ThrowAsync<InvalidOperationException>(() => engine.SwitchKeyAsync(OtherKey));
        (await engine.TryRefreshAsync()).ShouldBeFalse();
        _fixture.Http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ASwitch_WaitsForARunningStartup()
    {
        var gate = new GatedResponse(() => EngineFixture.Document("one", EngineFixture.Tag(1)));
        _fixture.Http.Respond(gate.RespondAsync);
        _fixture.RespondDocument("contoso", EngineFixture.Tag(2));
        var builder = new ConfigurationBuilder();
        var engine = _fixture.Add(builder);

        var building = Task.Run(() => _fixture.Track(builder.Build()));
        await gate.Entered;
        var switching = engine.SwitchKeyAsync(OtherKey);

        switching.IsCompleted.ShouldBeFalse();
        gate.Release();

        var root = await building.WaitAsync(EngineFixture.Patience);
        await switching.WaitAsync(EngineFixture.Patience);

        engine.ActiveKey.ShouldBe(OtherKey);
        root["App:Name"].ShouldBe("contoso");
    }

    [Fact]
    public async Task ACancelledSwitch_KeepsTheKey_AndCountsNoFailure()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build();
        var gate = new GatedResponse(() => EngineFixture.Document("contoso", EngineFixture.Tag(2)));
        _fixture.Http.Respond(gate.RespondAsync);
        using var cancellation = new CancellationTokenSource();

        var switching = engine.SwitchKeyAsync(OtherKey, cancellation.Token);
        await gate.Entered;
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => switching.WaitAsync(EngineFixture.Patience));
        engine.ActiveKey.ShouldBe(TestDocuments.Key);
        root["App:Name"].ShouldBe("one");
        engine.Status.ConsecutiveFailures.ShouldBe(0);
    }

    [Fact]
    public async Task ASwitch_SendsTheTagOfTheNewKeysProtectedEntry()
    {
        var protector = new TestCacheProtector();
        _fixture.RespondDocument("cached contoso", EngineFixture.Tag(5));
        using (var seeding = (IDisposable)new ConfigurationBuilder()
            .AddStoryteller(_fixture.Configure(options =>
            {
                options.Key = OtherKey;
                options.CacheDirectory = EngineFixture.CacheDirectory;
                options.CacheProtector = protector;
            }))
            .Build())
        {
        }

        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build(options =>
        {
            options.CacheDirectory = EngineFixture.CacheDirectory;
            options.CacheProtector = protector;
        });
        _fixture.RespondNotModified(EngineFixture.Tag(5));

        await engine.SwitchKeyAsync(OtherKey);

        _fixture.Http.Requests[^1].Headers["If-None-Match"].ShouldBe(EngineFixture.Tag(5));
        root["App:Name"].ShouldBe("cached contoso");
        engine.Status.Origin.ShouldBe(StorytellerConfigurationOrigin.Platform);
        engine.Status.ETag.ShouldBe(EngineFixture.Tag(5));
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }
}
