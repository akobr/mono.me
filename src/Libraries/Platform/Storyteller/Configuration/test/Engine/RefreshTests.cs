using System.ComponentModel.DataAnnotations;
using System.Net;
using _42.Platform.Storyteller.Sdk.Configuring.Caching;
using _42.Platform.Storyteller.Sdk.Configuring.Engine;
using _42.Platform.Storyteller.Sdk.Configuring.Reading;
using _42.Platform.Storyteller.Sdk.Credentials;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Engine;

public sealed class RefreshTests : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly EngineFixture _fixture = new();

    public enum Failure
    {
        InvalidDocument,
        Unprocessable,
        Unavailable,
        Withheld,
        CredentialRejected,
        CredentialUnavailable,
    }

    [Fact]
    public async Task A304_SendsTheStoredTag_AndDoesNotReload()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build();
        var changes = CountChanges(root);
        _fixture.RespondNotModified(EngineFixture.Tag(1));

        await _fixture.AdvanceAsync(engine, Interval);

        _fixture.Http.Requests.Count.ShouldBe(2);
        _fixture.Http.Requests[^1].Headers["If-None-Match"].ShouldBe(EngineFixture.Tag(1));
        changes().ShouldBe(0);
        root["App:Name"].ShouldBe("one");
        engine.Status.IsDegraded.ShouldBeFalse();
    }

    [Fact]
    public async Task AChange_ReloadsExactlyOnce()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build();
        var changes = CountChanges(root);
        _fixture.Time.Advance(TimeSpan.FromSeconds(5));
        _fixture.RespondDocument("two", EngineFixture.Tag(2), version: 4);

        (await engine.TryRefreshAsync()).ShouldBeTrue();

        changes().ShouldBe(1);
        root["App:Name"].ShouldBe("two");
        var status = engine.Status;
        status.ETag.ShouldBe(EngineFixture.Tag(2));
        status.LastAppliedAt.ShouldBe(_fixture.Time.GetUtcNow());
        status.LastSucceededAt.ShouldBe(_fixture.Time.GetUtcNow());
        status.IsDegraded.ShouldBeFalse();
    }

    [Fact]
    public async Task AnUnchangedDocumentWithANewTag_DoesNotReload()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build();
        var changes = CountChanges(root);
        _fixture.RespondDocument("one", EngineFixture.Tag(2), version: 4);

        (await engine.TryRefreshAsync()).ShouldBeTrue();

        changes().ShouldBe(0);
        engine.Status.ETag.ShouldBe(EngineFixture.Tag(2));
        engine.Snapshot.Document.ShouldNotBeNull().Version.ShouldBe(4);
    }

    [Fact]
    public async Task ARenamedProperty_IsApplied()
    {
        RespondFeatures("beta", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build();
        var changes = CountChanges(root);
        RespondFeatures("Beta", EngineFixture.Tag(2));

        (await engine.TryRefreshAsync()).ShouldBeTrue();

        changes().ShouldBe(1);
        root.GetSection("Features").GetChildren().Select(child => child.Key).ShouldBe(["Beta"]);
        root.GetSection("Features").Get<Dictionary<string, string>>().ShouldNotBeNull().Keys.ShouldBe(["Beta"]);
    }

    [Theory]
    [InlineData(Failure.InvalidDocument, false)]
    [InlineData(Failure.Unprocessable, false)]
    [InlineData(Failure.Unavailable, true)]
    [InlineData(Failure.Withheld, false)]
    [InlineData(Failure.CredentialRejected, false)]
    [InlineData(Failure.CredentialUnavailable, true)]
    public async Task AFailure_KeepsThePreviousData_AndIsCounted(Failure failure, bool isTransient)
    {
        var credential = new ToggleCredential();
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build(options => options.Credential = credential);
        var changes = CountChanges(root);
        _fixture.Time.Advance(TimeSpan.FromSeconds(5));

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            Arrange(failure, credential);

            (await engine.TryRefreshAsync()).ShouldBeFalse();

            root["App:Name"].ShouldBe("one");
            changes().ShouldBe(0);
            var status = engine.Status;
            status.LastError.ShouldNotBeNull().IsTransient.ShouldBe(isTransient);
            status.LastError.Key.ShouldBe(TestDocuments.Key);
            status.LastErrorAt.ShouldBe(_fixture.Time.GetUtcNow());
            status.ConsecutiveFailures.ShouldBe(attempt);
            status.TotalFailures.ShouldBe(attempt);
            status.IsDegraded.ShouldBeTrue();
            status.ETag.ShouldBe(EngineFixture.Tag(1));
            status.Origin.ShouldBe(StorytellerConfigurationOrigin.Platform);
        }
    }

    [Fact]
    public async Task ADocumentThatDoesNotBind_IsRolledBack_AndItsTagIsSentOnTheNextPoll()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build(options => options.CacheDirectory = EngineFixture.CacheDirectory);
        var services = new ServiceCollection();
        services.AddOptions<AppOptions>().Bind(root.GetSection("App")).ValidateDataAnnotations();
        using var container = services.BuildServiceProvider();
        var monitor = container.GetRequiredService<IOptionsMonitor<AppOptions>>();
        monitor.CurrentValue.Port.ShouldBe(80);
        _fixture.Time.Advance(TimeSpan.FromSeconds(5));

        // Port 0 fails the range of AppOptions.
        _fixture.RespondDocument("two", EngineFixture.Tag(2), version: 4, port: 0);

        (await engine.TryRefreshAsync()).ShouldBeFalse();

        monitor.CurrentValue.Name.ShouldBe("one");
        monitor.CurrentValue.Port.ShouldBe(80);
        root["App:Port"].ShouldBe("80");
        var status = engine.Status;
        status.IsDegraded.ShouldBeTrue();
        status.ETag.ShouldBe(EngineFixture.Tag(2));
        status.LastError.ShouldNotBeNull().Message.ShouldContain("did not bind");
        status.LastError.Message.ShouldContain(nameof(OptionsValidationException));
        status.LastError.IsTransient.ShouldBeFalse();
        ReadCacheEntry().ShouldNotBeNull().ETag.ShouldBe(EngineFixture.Tag(1));

        // The platform still has the rejected document: it answers 304 and nothing is applied again.
        _fixture.Time.Advance(TimeSpan.FromSeconds(5));
        _fixture.RespondNotModified(EngineFixture.Tag(2));

        (await engine.TryRefreshAsync()).ShouldBeTrue();

        _fixture.Http.Requests[^1].Headers["If-None-Match"].ShouldBe(EngineFixture.Tag(2));
        monitor.CurrentValue.Port.ShouldBe(80);
        status = engine.Status;
        status.IsDegraded.ShouldBeTrue();
        status.LastError.ShouldNotBeNull();
        status.LastSucceededAt.ShouldBe(_fixture.Time.GetUtcNow());

        // A new document is tried again.
        _fixture.RespondDocument("three", EngineFixture.Tag(3), version: 5, port: 81);

        (await engine.TryRefreshAsync()).ShouldBeTrue();

        monitor.CurrentValue.Name.ShouldBe("three");
        engine.Status.IsDegraded.ShouldBeFalse();
        ReadCacheEntry().ShouldNotBeNull().ETag.ShouldBe(EngineFixture.Tag(3));
    }

    [Fact]
    public async Task ARejectedDocumentAfterAnEmptyStart_IsAnswered304Later_WithoutAFailure()
    {
        _fixture.Http.Respond(HttpStatusCode.ServiceUnavailable);
        var (root, engine) = _fixture.Build(options => options.Optional = true);
        _fixture.Track(ChangeToken.OnChange(root.GetReloadToken, () =>
        {
            if (root["App:Name"] == "bad")
            {
                throw new InvalidOperationException("The document does not bind.");
            }
        }));
        _fixture.RespondDocument("bad", EngineFixture.Tag(2));

        (await engine.TryRefreshAsync()).ShouldBeFalse();

        root["App:Name"].ShouldBeNull();
        engine.Status.Origin.ShouldBe(StorytellerConfigurationOrigin.Empty);
        engine.Status.ETag.ShouldBe(EngineFixture.Tag(2));
        _fixture.RespondNotModified(EngineFixture.Tag(2));

        (await engine.TryRefreshAsync()).ShouldBeTrue();

        _fixture.Http.Requests[^1].Headers["If-None-Match"].ShouldBe(EngineFixture.Tag(2));
        var status = engine.Status;
        status.ConsecutiveFailures.ShouldBe(0);
        status.IsDegraded.ShouldBeTrue();
        status.Origin.ShouldBe(StorytellerConfigurationOrigin.Empty);
    }

    [Fact]
    public async Task ARollback_DoesNotUndoANewerSnapshotSwappedInMeanwhile()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build();
        var isReentered = false;

        _fixture.Track(ChangeToken.OnChange(root.GetReloadToken, () =>
        {
            if (root["App:Name"] == "bad" && !isReentered)
            {
                isReentered = true;

                // A newer document arrives while this one is notified, and then this one does not bind.
                engine.TryRefreshAsync().GetAwaiter().GetResult().ShouldBeTrue();
                throw new InvalidOperationException("The document does not bind.");
            }
        }));

        _fixture.RespondDocument("bad", EngineFixture.Tag(2), version: 4);
        _fixture.RespondDocument("three", EngineFixture.Tag(3), version: 5);

        (await engine.TryRefreshAsync().WaitAsync(EngineFixture.Patience)).ShouldBeFalse();

        root["App:Name"].ShouldBe("three");
        engine.Snapshot.ETag.ShouldBe(EngineFixture.Tag(3));
        engine.Snapshot.Rejection.ShouldBeNull();
        _fixture.Http.Requests[^1].Headers["If-None-Match"].ShouldBe(EngineFixture.Tag(2));
    }

    [Fact]
    public async Task ADocumentThatDoesNotBind_KeepsItsValuesOutOfTheErrorAndTheStatus()
    {
        const string Secret = "SuperSecretPa55word";
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build();
        var services = new ServiceCollection();
        services.AddOptions<AppOptions>().Bind(root.GetSection("App"));
        using var container = services.BuildServiceProvider();
        var monitor = container.GetRequiredService<IOptionsMonitor<AppOptions>>();
        monitor.CurrentValue.Port.ShouldBe(80);
        RespondApp(Secret, EngineFixture.Tag(2));

        (await engine.TryRefreshAsync()).ShouldBeFalse();

        var error = engine.Status.LastError.ShouldNotBeNull();
        error.Message.ShouldContain("did not bind");
        error.InnerException.ShouldBeNull();
        error.ToString().ShouldNotContain(Secret);
        engine.Status.ToString().ShouldNotContain(Secret);
        engine.Status.ToString().ShouldContain(error.Message);

        // A switch to a document that does not bind throws the same kind of error to its caller.
        RespondApp(Secret, EngineFixture.Tag(3));

        var switchError = await Should.ThrowAsync<StorytellerConfigurationException>(() => engine.SwitchKeyAsync("sbt.contoso"));

        switchError.ToString().ShouldNotContain(Secret);
        engine.ActiveKey.ShouldBe(TestDocuments.Key);
        monitor.CurrentValue.Port.ShouldBe(80);
        _fixture.Logs.Entries.ShouldAllBe(entry => !entry.Message.Contains(Secret) && (entry.Exception == null || !entry.Exception.ToString().Contains(Secret)));
    }

    [Fact]
    public async Task AMetadataOnlyChange_WhileADocumentThatDoesNotBindIsNotified_IsRolledBackWithIt_AndNotCached()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build(options => options.CacheDirectory = EngineFixture.CacheDirectory);
        var isReentered = false;
        var nested = false;

        _fixture.Track(ChangeToken.OnChange(root.GetReloadToken, () =>
        {
            if (root["App:Name"] == "bad" && !isReentered)
            {
                isReentered = true;

                // The same document under a new tag arrives meanwhile: it shares the data, so nothing reloads for it.
                nested = engine.TryRefreshAsync().GetAwaiter().GetResult();
                throw new InvalidOperationException("The document does not bind.");
            }
        }));

        _fixture.RespondDocument("bad", EngineFixture.Tag(2), version: 4);
        _fixture.RespondDocument("bad", EngineFixture.Tag(3), version: 5);

        (await engine.TryRefreshAsync().WaitAsync(EngineFixture.Patience)).ShouldBeFalse();

        nested.ShouldBeTrue();
        root["App:Name"].ShouldBe("one");
        engine.Snapshot.Rejection.ShouldNotBeNull();
        engine.Snapshot.ETag.ShouldBe(EngineFixture.Tag(3));
        ReadCacheEntry().ShouldNotBeNull().ETag.ShouldBe(EngineFixture.Tag(1));

        // The platform still has it: a 304 keeps the restored document, and the status degraded.
        _fixture.RespondNotModified(EngineFixture.Tag(3));

        (await engine.TryRefreshAsync()).ShouldBeTrue();

        _fixture.Http.Requests[^1].Headers["If-None-Match"].ShouldBe(EngineFixture.Tag(3));
        root["App:Name"].ShouldBe("one");
        engine.Status.IsDegraded.ShouldBeTrue();
    }

    [Fact]
    public async Task ADocumentAnotherRootRejects_WhileOneRootStillHandlesIt_IsRolledBackOnBoth_AndNeverCached()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var builder = new ConfigurationBuilder();
        var engine = _fixture.Add(builder, options => options.CacheDirectory = EngineFixture.CacheDirectory);
        var first = _fixture.Track(builder.Build());
        var second = _fixture.Track(builder.Build());
        using var container = CreateContainer(second);
        var monitor = container.GetRequiredService<IOptionsMonitor<AppOptions>>();
        monitor.CurrentValue.Port.ShouldBe(80);
        var isReentered = false;
        var nested = new List<bool>();

        _fixture.Track(ChangeToken.OnChange(first.GetReloadToken, () =>
        {
            if (first["App:Port"] == "0" && !isReentered)
            {
                isReentered = true;

                // The first root is slow, and the same document arrives under new tags meanwhile. The nested runs end at
                // their swap; the pass goes on to the second root, whose options port 0 fails.
                nested.Add(engine.TryRefreshAsync().GetAwaiter().GetResult());
                nested.Add(engine.TryRefreshAsync().GetAwaiter().GetResult());
            }
        }));

        _fixture.RespondDocument("two", EngineFixture.Tag(2), version: 4, port: 0);
        _fixture.RespondDocument("two", EngineFixture.Tag(3), version: 5, port: 0);
        _fixture.RespondDocument("two", EngineFixture.Tag(4), version: 6, port: 0);

        (await engine.TryRefreshAsync().WaitAsync(EngineFixture.Patience)).ShouldBeFalse();

        // Nested runs do not wait for the notification, so they report their reads.
        nested.ShouldBe([true, true]);
        first["App:Port"].ShouldBe("80");
        second["App:Port"].ShouldBe("80");
        monitor.CurrentValue.Port.ShouldBe(80);
        engine.Snapshot.Rejection.ShouldNotBeNull();
        engine.Snapshot.ETag.ShouldBe(EngineFixture.Tag(4));
        engine.Status.LastError.ShouldNotBeNull().Message.ShouldContain("did not bind");
        engine.Status.TotalFailures.ShouldBe(1);
        ReadCacheEntry().ShouldNotBeNull().ETag.ShouldBe(EngineFixture.Tag(1));

        // The platform keeps the document: a 304 keeps the restored one, and nothing reaches the cache.
        _fixture.RespondNotModified(EngineFixture.Tag(4));

        (await engine.TryRefreshAsync()).ShouldBeTrue();

        _fixture.Http.Requests[^1].Headers["If-None-Match"].ShouldBe(EngineFixture.Tag(4));
        second["App:Port"].ShouldBe("80");
        monitor.CurrentValue.Port.ShouldBe(80);
        engine.Status.IsDegraded.ShouldBeTrue();
        ReadCacheEntry().ShouldNotBeNull().ETag.ShouldBe(EngineFixture.Tag(1));
    }

    // Notifications are serialized: "two" reaches every root before "three" is notified, so the rollback goes to "two",
    // the run of "two" succeeds, and the one rejection is counted once.
    [Fact]
    public async Task ADocumentEveryRootTook_BeforeANewerOneIsRejected_IsTheRollbackTarget_AndTheRejectionCountsOnce()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var builder = new ConfigurationBuilder();
        var engine = _fixture.Add(builder, options => options.CacheDirectory = EngineFixture.CacheDirectory);
        var first = _fixture.Track(builder.Build());
        var second = _fixture.Track(builder.Build());
        using var container = CreateContainer(second);
        var monitor = container.GetRequiredService<IOptionsMonitor<AppOptions>>();
        monitor.CurrentValue.Port.ShouldBe(80);
        var isReentered = false;
        bool? nested = null;

        _fixture.Track(ChangeToken.OnChange(first.GetReloadToken, () =>
        {
            if (first["App:Name"] == "two" && !isReentered)
            {
                isReentered = true;

                // Before the second root saw "two", "three" arrives, and port 0 fails the second root's options.
                nested = engine.TryRefreshAsync().GetAwaiter().GetResult();
            }
        }));

        _fixture.RespondDocument("two", EngineFixture.Tag(2), version: 4, port: 81);
        _fixture.RespondDocument("three", EngineFixture.Tag(3), version: 5, port: 0);

        (await engine.TryRefreshAsync().WaitAsync(EngineFixture.Patience)).ShouldBeTrue();

        nested.ShouldBe(true);
        first["App:Name"].ShouldBe("two");
        second["App:Name"].ShouldBe("two");
        monitor.CurrentValue.Name.ShouldBe("two");
        engine.Snapshot.ETag.ShouldBe(EngineFixture.Tag(3));
        engine.Snapshot.Rejection.ShouldNotBeNull();
        engine.Status.IsDegraded.ShouldBeTrue();
        engine.Status.TotalFailures.ShouldBe(1);
        ReadCacheEntry().ShouldNotBeNull().ETag.ShouldBe(EngineFixture.Tag(2));
    }

    // A slow change listener (one that reconnects a client, say) still handles "two" when another run applies "three",
    // which fails validation. The other run waits for the lane, so the rejection is the pass of "three".
    [Fact]
    public async Task ADocumentThatDoesNotBind_WhileASlowListenerHandlesThePreviousOne_IsRolledBack_AndNotCached()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build(options => options.CacheDirectory = EngineFixture.CacheDirectory);
        using var container = CreateContainer(root);
        var monitor = container.GetRequiredService<IOptionsMonitor<AppOptions>>();
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
        _fixture.RespondDocument("three", EngineFixture.Tag(3), version: 5, port: 0);

        var first = engine.TryRefreshAsync();
        await entered.Task.WaitAsync(EngineFixture.Patience);
        var second = engine.TryRefreshAsync();
        SpinWait.SpinUntil(() => engine.Snapshot.ETag == EngineFixture.Tag(3), EngineFixture.Patience).ShouldBeTrue();

        // Swapped in, and waiting for the notification of "two" to end.
        second.IsCompleted.ShouldBeFalse();
        release.SetResult();

        (await first.WaitAsync(EngineFixture.Patience)).ShouldBeTrue();
        (await second.WaitAsync(EngineFixture.Patience)).ShouldBeFalse();
        AssertRolledBackToTwo(root, engine, monitor);

        await AssertDegradedUntilAValidDocumentAsync(engine, monitor);
    }

    // The same from inside an IOptionsMonitor listener: its refresh fetches "three", which fails validation. The nested run
    // ends at its swap, and the pass that follows the one of "two" rejects it.
    [Fact]
    public async Task ADocumentThatDoesNotBind_FetchedFromAnOptionsListener_IsRolledBack_AndNotCached()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build(options => options.CacheDirectory = EngineFixture.CacheDirectory);
        using var container = CreateContainer(root);
        var monitor = container.GetRequiredService<IOptionsMonitor<AppOptions>>();
        monitor.CurrentValue.Port.ShouldBe(80);
        bool? nested = null;

        using var listener = monitor.OnChange(options =>
        {
            if (options.Name == "two" && nested is null)
            {
                nested = engine.TryRefreshAsync().GetAwaiter().GetResult();
            }
        });

        _fixture.RespondDocument("two", EngineFixture.Tag(2), version: 4, port: 81);
        _fixture.RespondDocument("three", EngineFixture.Tag(3), version: 5, port: 0);

        (await engine.TryRefreshAsync().WaitAsync(EngineFixture.Patience)).ShouldBeTrue();

        nested.ShouldBe(true);
        AssertRolledBackToTwo(root, engine, monitor);

        await AssertDegradedUntilAValidDocumentAsync(engine, monitor);
    }

    // A run that read from the snapshot of a document that is then rejected gets the same document under a new tag. The
    // root that rejected it is gone, so it is tried again, applied, confirmed and cached.
    [Fact]
    public async Task TheRejectedDocumentUnderANewTag_IsTriedAgain_AndAppliedOnceEveryRootTakesIt()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var builder = new ConfigurationBuilder();
        var engine = _fixture.Add(builder, options => options.CacheDirectory = EngineFixture.CacheDirectory);
        var first = _fixture.Track(builder.Build());
        var second = builder.Build();
        var container = CreateContainer(second);
        container.GetRequiredService<IOptionsMonitor<AppOptions>>().CurrentValue.Port.ShouldBe(80);
        var gate = new GatedResponse(() => EngineFixture.Document("two", EngineFixture.Tag(3), version: 5, port: 0));
        Task<bool>? nested = null;

        _fixture.Track(ChangeToken.OnChange(first.GetReloadToken, () =>
        {
            if (first["App:Port"] == "0" && nested is null)
            {
                nested = engine.TryRefreshAsync();
                gate.Entered.GetAwaiter().GetResult();
            }
        }));

        _fixture.RespondDocument("two", EngineFixture.Tag(2), version: 4, port: 0);
        _fixture.Http.Respond(gate.RespondAsync);

        (await engine.TryRefreshAsync().WaitAsync(EngineFixture.Patience)).ShouldBeFalse();

        first["App:Port"].ShouldBe("80");
        container.Dispose();
        ((IDisposable)second).Dispose();
        gate.Release();

        (await nested.ShouldNotBeNull().WaitAsync(EngineFixture.Patience)).ShouldBeTrue();
        first["App:Port"].ShouldBe("0");
        engine.Snapshot.Rejection.ShouldBeNull();
        var status = engine.Status;
        status.IsDegraded.ShouldBeFalse();
        status.TotalFailures.ShouldBe(1);
        ReadCacheEntry().ShouldNotBeNull().ETag.ShouldBe(EngineFixture.Tag(3));
    }

    // The last root goes while "two" is notified, and a new root restarts the engine. It loaded "two", so a rejected
    // document rolls back to "two".
    [Fact]
    public async Task ARestart_AfterTheLastRootWentDuringANotification_RollsBackToTheDocumentTheNewRootLoaded()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var builder = new ConfigurationBuilder();
        var engine = _fixture.Add(builder);
        var first = builder.Build();

        using (ChangeToken.OnChange(first.GetReloadToken, () =>
        {
            if (first["App:Name"] == "two")
            {
                ((IDisposable)first).Dispose();
            }
        }))
        {
            _fixture.RespondDocument("two", EngineFixture.Tag(2), version: 4);
            (await engine.TryRefreshAsync().WaitAsync(EngineFixture.Patience)).ShouldBeTrue();
        }

        engine.IsRunning.ShouldBeFalse();
        var second = _fixture.Track(builder.Build());
        second["App:Name"].ShouldBe("two");
        using var container = CreateContainer(second);
        var monitor = container.GetRequiredService<IOptionsMonitor<AppOptions>>();
        monitor.CurrentValue.Name.ShouldBe("two");
        _fixture.RespondDocument("three", EngineFixture.Tag(3), version: 5, port: 0);

        (await engine.TryRefreshAsync()).ShouldBeFalse();

        second["App:Name"].ShouldBe("two");
        monitor.CurrentValue.Name.ShouldBe("two");
        engine.Snapshot.Rejection.ShouldNotBeNull();
    }

    // A change callback waits for a TryRefreshAsync run that an outside caller queued: the shared run must not wait for
    // the lane that the callback's notification holds.
    [Fact]
    public async Task ARefreshFromAChangeCallback_ThatJoinsAQueuedRun_DoesNotDeadlock()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var join = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool? nested = null;

        _fixture.Track(ChangeToken.OnChange(root.GetReloadToken, () =>
        {
            if (root["App:Name"] == "two" && entered.TrySetResult())
            {
                join.Task.Wait(EngineFixture.Patience);
                var refresh = engine.TryRefreshAsync();
                joined.SetResult();

                // Bounded, so a deadlock fails the test instead of hanging it.
                nested = refresh.Wait(EngineFixture.Patience) && refresh.Result;
            }
        }));

        var gate = new GatedResponse(() => EngineFixture.Document("three", EngineFixture.Tag(3), version: 5));
        _fixture.RespondDocument("two", EngineFixture.Tag(2), version: 4);
        _fixture.Http.Respond(gate.RespondAsync);
        _fixture.RespondDocument("four", EngineFixture.Tag(4), version: 6);

        var notifying = engine.TryRefreshAsync();
        await entered.Task.WaitAsync(EngineFixture.Patience);

        // A run holds the gate, so the next one is queued, and the callback joins it.
        var holding = engine.TryRefreshAsync();
        await gate.Entered;
        var queued = engine.TryRefreshAsync();
        join.SetResult();
        await joined.Task.WaitAsync(EngineFixture.Patience);
        gate.Release();

        (await notifying.WaitAsync(EngineFixture.Patience)).ShouldBeTrue();
        (await holding.WaitAsync(EngineFixture.Patience)).ShouldBeTrue();
        (await queued.WaitAsync(EngineFixture.Patience)).ShouldBeTrue();
        nested.ShouldBe(true);
        root["App:Name"].ShouldBe("four");
        _fixture.Http.Requests.Count.ShouldBe(4);
    }

    [Fact]
    public async Task AMetadataOnlyChange_WhileADocumentIsNotified_IsCachedOnceTheDocumentBound()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build(options => options.CacheDirectory = EngineFixture.CacheDirectory);
        var isReentered = false;
        var nested = false;

        _fixture.Track(ChangeToken.OnChange(root.GetReloadToken, () =>
        {
            if (root["App:Name"] == "two" && !isReentered)
            {
                isReentered = true;

                // Its data is still being reloaded here, so its cache write waits for this notification to end.
                nested = engine.TryRefreshAsync().GetAwaiter().GetResult();
                ReadCacheEntry().ShouldNotBeNull().ETag.ShouldBe(EngineFixture.Tag(1));
            }
        }));

        _fixture.RespondDocument("two", EngineFixture.Tag(2), version: 4);
        _fixture.RespondDocument("two", EngineFixture.Tag(3), version: 5);

        (await engine.TryRefreshAsync().WaitAsync(EngineFixture.Patience)).ShouldBeTrue();

        nested.ShouldBeTrue();
        root["App:Name"].ShouldBe("two");
        engine.Snapshot.ETag.ShouldBe(EngineFixture.Tag(3));
        ReadCacheEntry().ShouldNotBeNull().ETag.ShouldBe(EngineFixture.Tag(3));
    }

    [Fact]
    public async Task A304ThatMayNotBeStored_WhileA200IsNotified_LeavesNoPlainEntry()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build(options => options.CacheDirectory = EngineFixture.CacheDirectory);
        var isReentered = false;
        var nested = false;

        _fixture.Track(ChangeToken.OnChange(root.GetReloadToken, () =>
        {
            if (root["App:Name"] == "two" && !isReentered)
            {
                isReentered = true;
                nested = engine.TryRefreshAsync().GetAwaiter().GetResult();
            }
        }));

        _fixture.RespondDocument("two", EngineFixture.Tag(2), version: 4);
        _fixture.RespondNotModified(EngineFixture.Tag(2), "no-store");

        (await engine.TryRefreshAsync().WaitAsync(EngineFixture.Patience)).ShouldBeTrue();

        nested.ShouldBeTrue();
        root["App:Name"].ShouldBe("two");
        engine.Snapshot.CacheControl.ShouldBe("no-store");
        _fixture.FileSystem.Directory.GetFiles(EngineFixture.CacheDirectory, "*.cache").ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.999999)]
    public void TheJitter_StaysWithinTenPercent(double random)
    {
        var delay = StorytellerConfigurationEngine.ComputeDelay(Interval, random);

        delay.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(27));
        delay.ShouldBeLessThan(TimeSpan.FromSeconds(33));
    }

    [Theory]
    [InlineData(45 * 24, 0.999999)]
    [InlineData(1_000_000, 0.0)]
    public void TheDelay_StaysWithinWhatASystemTimerTakes(int hours, double random)
    {
        var limit = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

        var delay = StorytellerConfigurationEngine.ComputeDelay(TimeSpan.FromHours(hours), random);

        delay.ShouldBeLessThanOrEqualTo(limit);
        delay.ShouldBeGreaterThan(TimeSpan.FromDays(40));
        StorytellerConfigurationEngine.ComputeDelay(TimeSpan.MaxValue, random).ShouldBe(limit);
        using var timer = TimeProvider.System.CreateTimer(_ => { }, null, delay, Timeout.InfiniteTimeSpan);
    }

    [Theory]
    [InlineData(0.0, 26_999, 27_000)]
    [InlineData(0.999999, 32_999, 33_000)]
    public async Task TheTimer_FiresAfterTheJitteredInterval(double random, int beforeMilliseconds, int afterMilliseconds)
    {
        _fixture.Random = random;
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (_, engine) = _fixture.Build();
        _fixture.Http.Otherwise = (_, _) => Task.FromResult(EngineFixture.NotModified(EngineFixture.Tag(1)));

        await _fixture.AdvanceAsync(engine, TimeSpan.FromMilliseconds(beforeMilliseconds));
        _fixture.Http.Requests.Count.ShouldBe(1);

        await _fixture.AdvanceAsync(engine, TimeSpan.FromMilliseconds(afterMilliseconds - beforeMilliseconds));
        _fixture.Http.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Runs_NeverOverlap()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (_, engine) = _fixture.Build();
        var inFlight = 0;
        var maxInFlight = 0;
        var requests = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _fixture.Http.Otherwise = async (_, cancellationToken) =>
        {
            var current = Interlocked.Increment(ref inFlight);
            InterlockedMax(ref maxInFlight, current);

            try
            {
                var number = Interlocked.Increment(ref requests);

                if (number == 1)
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(cancellationToken);
                }
                else
                {
                    await Task.Yield();
                }

                return EngineFixture.Document($"n{number}", EngineFixture.Tag(10 + number), version: 10 + number);
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
            }
        };

        var refresh = engine.TryRefreshAsync();
        await entered.Task.WaitAsync(EngineFixture.Patience);
        _fixture.Time.Advance(Interval);
        var timerRun = engine.TimerRun;
        var switching = engine.SwitchKeyAsync("sbt.contoso");
        var secondRefresh = engine.TryRefreshAsync();
        release.TrySetResult();

        await Task.WhenAll(refresh, timerRun, switching, secondRefresh).WaitAsync(EngineFixture.Patience);

        maxInFlight.ShouldBe(1);
        requests.ShouldBe(4);
    }

    [Fact]
    public async Task AZeroRefreshInterval_StartsNoTimer()
    {
        var time = new CountingTimeProvider();
        using var fixture = new EngineFixture(time);
        fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (_, engine) = fixture.Build(options => options.RefreshInterval = TimeSpan.Zero);

        await fixture.AdvanceAsync(engine, TimeSpan.FromHours(1));

        time.Created.ShouldBe(0);
        fixture.Http.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ConcurrentTryRefreshCalls_ShareOneQueuedRun_ButNeverOneThatSentItsRequest()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build();
        var gate = new GatedResponse(() => EngineFixture.Document("two", EngineFixture.Tag(2), version: 4));
        _fixture.Http.Respond(gate.RespondAsync);
        _fixture.RespondDocument("three", EngineFixture.Tag(3), version: 5);

        var first = engine.TryRefreshAsync();
        await gate.Entered;
        var second = engine.TryRefreshAsync();
        var third = engine.TryRefreshAsync();
        gate.Release();

        (await first.WaitAsync(EngineFixture.Patience)).ShouldBeTrue();
        (await second.WaitAsync(EngineFixture.Patience)).ShouldBeTrue();
        (await third.WaitAsync(EngineFixture.Patience)).ShouldBeTrue();

        // The startup, the first run, and one run that the two later calls shared, read after both calls.
        _fixture.Http.Requests.Count.ShouldBe(3);
        root["App:Name"].ShouldBe("three");
    }

    [Fact]
    public async Task InterleavedPublishesAndLoads_LeaveEachProviderOnTheNewestSnapshot()
    {
        var builder = new ConfigurationBuilder();
        var engine = _fixture.Add(builder);
        _fixture.RespondDocument("v0", EngineFixture.Tag(100));
        var first = _fixture.Track(builder.Build());
        var second = _fixture.Track(builder.Build());
        var version = 0;
        _fixture.Http.Otherwise = (_, _) =>
        {
            var number = Interlocked.Increment(ref version);
            return Task.FromResult(EngineFixture.Document($"v{number}", EngineFixture.Tag(number), version: number));
        };
        using var stop = new CancellationTokenSource();

        var loads = Task.Run(
            () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    second.Reload();
                    first.Reload();
                }
            },
            CancellationToken.None);

        for (var run = 0; run < 20; run++)
        {
            (await engine.TryRefreshAsync()).ShouldBeTrue();
        }

        await stop.CancelAsync();
        await loads.WaitAsync(EngineFixture.Patience);

        first["App:Name"].ShouldBe("v20");
        second["App:Name"].ShouldBe("v20");
    }

    [Fact]
    public void AProvider_NeverPublishesAnOlderSnapshot_AndReloadsOnlyForNewData()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build();
        var provider = root.Providers.OfType<StorytellerConfigurationProvider>().Single();
        var current = engine.Snapshot;
        var newer = current with
        {
            Sequence = current.Sequence + 10,
            Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["App:Name"] = "newer" },
        };

        provider.Publish(newer).ShouldBeTrue();
        provider.Publish(current).ShouldBeFalse();
        root["App:Name"].ShouldBe("newer");

        // Only the metadata changed.
        provider.Publish(newer with { Sequence = current.Sequence + 11, ETag = EngineFixture.Tag(9) }).ShouldBeFalse();
        provider.Publish(newer with { Sequence = current.Sequence + 11 }).ShouldBeFalse();
    }

    [Fact]
    public void AValueSetOnOneRoot_DoesNotLeakIntoAnother()
    {
        var builder = new ConfigurationBuilder();
        _fixture.Add(builder);
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var first = _fixture.Track(builder.Build());
        var second = _fixture.Track(builder.Build());

        first["App:Name"] = "local";

        first["App:Name"].ShouldBe("local");
        second["App:Name"].ShouldBe("one");
    }

    [Fact]
    public async Task TheFirstPollAfterAStartFromAPlainEntry_IsUnconditional()
    {
        SeedCache();
        _fixture.Http.Respond(HttpStatusCode.ServiceUnavailable);
        var (root, engine) = _fixture.Build(options => options.CacheDirectory = EngineFixture.CacheDirectory);
        engine.Status.Origin.ShouldBe(StorytellerConfigurationOrigin.Cache);
        _fixture.RespondDocument("two", EngineFixture.Tag(2), version: 4);

        await _fixture.AdvanceAsync(engine, Interval);

        _fixture.Http.Requests[^1].Headers.ContainsKey("If-None-Match").ShouldBeFalse();
        root["App:Name"].ShouldBe("two");
        engine.Status.Origin.ShouldBe(StorytellerConfigurationOrigin.Platform);
        engine.Status.IsDegraded.ShouldBeFalse();
    }

    [Fact]
    public async Task A304ThatConfirmsAProtectedEntry_TurnsTheOriginToPlatform_WithoutAReload()
    {
        var protector = new TestCacheProtector();
        SeedCache(protector);
        _fixture.Http.Respond(HttpStatusCode.ServiceUnavailable);
        var (root, engine) = _fixture.Build(options =>
        {
            options.CacheDirectory = EngineFixture.CacheDirectory;
            options.CacheProtector = protector;
        });
        engine.Status.Origin.ShouldBe(StorytellerConfigurationOrigin.Cache);
        engine.Status.ETag.ShouldBe(EngineFixture.Tag(1));
        var changes = CountChanges(root);
        _fixture.RespondNotModified(EngineFixture.Tag(1));

        await _fixture.AdvanceAsync(engine, Interval);

        _fixture.Http.Requests[^1].Headers["If-None-Match"].ShouldBe(EngineFixture.Tag(1));
        changes().ShouldBe(0);
        root["App:Name"].ShouldBe("cached");
        engine.Status.Origin.ShouldBe(StorytellerConfigurationOrigin.Platform);
        engine.Status.IsDegraded.ShouldBeFalse();
    }

    [Theory]
    [InlineData("no-store")]
    [InlineData(null)]
    public async Task A304ThatMayNotBeStored_DeletesThePlainEntry(string? cacheControl)
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (_, engine) = _fixture.Build(options => options.CacheDirectory = EngineFixture.CacheDirectory);
        _fixture.FileSystem.Directory.GetFiles(EngineFixture.CacheDirectory, "*.cache").ShouldHaveSingleItem();
        _fixture.RespondNotModified(EngineFixture.Tag(1), cacheControl);

        (await engine.TryRefreshAsync()).ShouldBeTrue();

        _fixture.FileSystem.Directory.GetFiles(EngineFixture.CacheDirectory, "*.cache").ShouldBeEmpty();
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }

    private static void InterlockedMax(ref int target, int value)
    {
        var current = Volatile.Read(ref target);

        while (value > current)
        {
            var seen = Interlocked.CompareExchange(ref target, value, current);

            if (seen == current)
            {
                return;
            }

            current = seen;
        }
    }

    // Options of the root that a document with port 0 fails.
    private static ServiceProvider CreateContainer(IConfiguration root)
    {
        var services = new ServiceCollection();
        services.AddOptions<AppOptions>().Bind(root.GetSection("App")).ValidateDataAnnotations();
        return services.BuildServiceProvider();
    }

    private Func<int> CountChanges(IConfigurationRoot root)
    {
        var count = 0;
        _fixture.Track(ChangeToken.OnChange(root.GetReloadToken, () => Interlocked.Increment(ref count)));
        return () => Volatile.Read(ref count);
    }

    // "three" (port 0) did not bind: the options work and show "two", which is cached; "three" is not.
    private void AssertRolledBackToTwo(IConfigurationRoot root, StorytellerConfigurationEngine engine, IOptionsMonitor<AppOptions> monitor)
    {
        monitor.CurrentValue.Name.ShouldBe("two");
        monitor.CurrentValue.Port.ShouldBe(81);
        root["App:Port"].ShouldBe("81");
        engine.Snapshot.ETag.ShouldBe(EngineFixture.Tag(3));
        engine.Snapshot.Rejection.ShouldNotBeNull();
        var status = engine.Status;
        status.IsDegraded.ShouldBeTrue();
        status.TotalFailures.ShouldBe(1);
        status.LastError.ShouldNotBeNull().Message.ShouldContain(nameof(OptionsValidationException));
        ReadCacheEntry().ShouldNotBeNull().ETag.ShouldBe(EngineFixture.Tag(2));
    }

    // The platform keeps "three": a 304 keeps "two" and the status degraded. A valid document ends it.
    private async Task AssertDegradedUntilAValidDocumentAsync(StorytellerConfigurationEngine engine, IOptionsMonitor<AppOptions> monitor)
    {
        _fixture.RespondNotModified(EngineFixture.Tag(3));

        (await engine.TryRefreshAsync()).ShouldBeTrue();

        _fixture.Http.Requests[^1].Headers["If-None-Match"].ShouldBe(EngineFixture.Tag(3));
        monitor.CurrentValue.Name.ShouldBe("two");
        engine.Status.IsDegraded.ShouldBeTrue();
        ReadCacheEntry().ShouldNotBeNull().ETag.ShouldBe(EngineFixture.Tag(2));
        _fixture.RespondDocument("four", EngineFixture.Tag(4), version: 6, port: 82);

        (await engine.TryRefreshAsync()).ShouldBeTrue();

        monitor.CurrentValue.Name.ShouldBe("four");
        engine.Status.IsDegraded.ShouldBeFalse();
        ReadCacheEntry().ShouldNotBeNull().ETag.ShouldBe(EngineFixture.Tag(4));
    }

    private void RespondFeatures(string name, string etag)
    {
        _fixture.Http.Respond(
            HttpStatusCode.OK,
            TestDocuments.Json($$"""{ "Features": { "{{name}}": "true" } }"""),
            ("ETag", etag),
            ("Cache-Control", EngineFixture.PrivateNoCache));
    }

    private void RespondApp(string port, string etag)
    {
        _fixture.Http.Respond(
            HttpStatusCode.OK,
            TestDocuments.Json($$"""{ "App": { "Name": "two", "Port": "{{port}}" } }"""),
            ("ETag", etag),
            ("Cache-Control", EngineFixture.PrivateNoCache));
    }

    private void Arrange(Failure failure, ToggleCredential credential)
    {
        switch (failure)
        {
            case Failure.InvalidDocument:
                _fixture.Http.Respond(HttpStatusCode.OK, TestDocuments.Json("""{ "App": { "": 1 } }"""), ("ETag", EngineFixture.Tag(2)));
                break;

            case Failure.Unprocessable:
                _fixture.Http.Respond(HttpStatusCode.UnprocessableEntity, """{ "Message": "Cannot evaluate.", "ErrorCode": "binding.evaluation" }""");
                break;

            case Failure.Unavailable:
                _fixture.Http.Respond(HttpStatusCode.ServiceUnavailable);
                break;

            case Failure.Withheld:
                _fixture.Http.Respond(HttpStatusCode.OK, TestDocuments.Json(), ("ETag", EngineFixture.Tag(2)), ("X-2S-Secrets-Withheld", "mtls-required"));
                break;

            case Failure.CredentialRejected:
                credential.Failure = StorytellerAuthenticationFailureReason.Rejected;
                break;

            case Failure.CredentialUnavailable:
                credential.Failure = StorytellerAuthenticationFailureReason.Unavailable;
                break;
        }
    }

    private void SeedCache(IStorytellerCacheProtector? protector = null)
    {
        _fixture.RespondDocument("cached", EngineFixture.Tag(1));

        using var root = (IDisposable)new ConfigurationBuilder()
            .AddStoryteller(_fixture.Configure(options =>
            {
                options.CacheDirectory = EngineFixture.CacheDirectory;
                options.CacheProtector = protector;
            }))
            .Build();
    }

    private CacheEntry? ReadCacheEntry()
    {
        var cache = new LastKnownGoodCache(EngineFixture.CacheDirectory, _fixture.FileSystem, null, _fixture.Time, NullLogger.Instance);
        var identity = DocumentIdentity.Create(new Uri(TestDocuments.Endpoint), FullKey.Parse(TestDocuments.Key), StorytellerReadMode.Resolved);
        return cache.Read(new CacheIdentity(identity, CredentialFingerprint.Anonymous));
    }

    public sealed class AppOptions
    {
        [Required]
        public string? Name { get; set; }

        [Range(1, 65535)]
        public int Port { get; set; }
    }
}
