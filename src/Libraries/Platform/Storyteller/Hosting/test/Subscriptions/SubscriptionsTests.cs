using System.Diagnostics;
using System.Net;
using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Informer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Subscriptions;

public sealed class SubscriptionsTests : IDisposable
{
    private const string LogCategory = "_42.Platform.Storyteller.Sdk.Hosting.Subscriptions";

    private readonly HostingFixture _fixture = new();
    private readonly FakeCatalog _catalog;
    private readonly FakeConfigurations _configurations;

    public SubscriptionsTests()
    {
        _catalog = new FakeCatalog(_fixture);
        _configurations = new FakeConfigurations(_fixture);
    }

    private DateTimeOffset Now => _fixture.Time.GetUtcNow();

    [Fact]
    public async Task TheFirstPoll_AddsEverySubscription_WithItsDocument_FoundBySubject()
    {
        Subscribe("northwind");
        Subscribe("contoso");
        await using var harness = new SubscriptionsHarness(_fixture);

        await harness.StartAndPollAsync();

        var subscriptions = harness.Subscriptions;
        subscriptions.Responsibility.ShouldBe(FakeCatalog.Responsibility);
        subscriptions.Context.ShouldBe(FakeCatalog.Context);
        subscriptions.GetAll().Select(subscription => subscription.Subject).ShouldBe(["contoso", "northwind"]);
        var northwind = subscriptions.Find("northwind").ShouldNotBeNull();
        northwind.Execution.Key.ShouldBe(Full(Execution("northwind")));
        northwind.Usage.Key.ShouldBe(Full(Usage("northwind")));
        northwind.Document.Key.ShouldBe(Full(Execution("northwind")));
        northwind.Document.Mode.ShouldBe(StorytellerReadMode.Resolved);
        northwind.Document.ETag.ShouldBe(_configurations.Tag(Execution("northwind")));
        northwind.Document.Configuration["App:Name"].ShouldBe("northwind");
        northwind.LastError.ShouldBeNull();
        northwind.LastErrorAt.ShouldBeNull();
        northwind.LastSucceededAt.ShouldBe(Now);
        northwind.ToString().ShouldBe($"{Full(Execution("northwind"))} (northwind)");
        subscriptions.Find("Northwind").ShouldBeNull();
        subscriptions.Find("fabrikam").ShouldBeNull();

        var status = subscriptions.Status;
        status.IsReady.ShouldBeTrue();
        status.LastPollSucceededAt.ShouldBe(Now);
        status.LastPollError.ShouldBeNull();
        status.Subscriptions.ShouldBe(2);
        status.Pending.ShouldBeEmpty();
        status.Failing.ShouldBe(0);
        status.IsDegraded.ShouldBeFalse();
        _configurations.Reads(Execution("northwind")).ShouldHaveSingleItem().Headers.ShouldNotContainKey("If-None-Match");
    }

    [Fact]
    public async Task ASubscriptionWhoseFirstReadFails_IsPending_WithItsError_UntilAReadSucceeds()
    {
        Subscribe("northwind");
        Subscribe("contoso");
        _configurations.Fail(Execution("contoso"), HttpStatusCode.ServiceUnavailable, TestAnnotations.ErrorJson);
        await using var harness = new SubscriptionsHarness(_fixture);
        await using var watch = new WatchProbe(harness.Subscriptions);

        await harness.StartAndPollAsync();

        (await watch.WaitForAsync(1)).ShouldHaveSingleItem().Subscription.Subject.ShouldBe("northwind");
        harness.Subscriptions.GetAll().ShouldHaveSingleItem().Subject.ShouldBe("northwind");
        harness.Subscriptions.Find("contoso").ShouldBeNull();
        var status = harness.Subscriptions.Status;
        status.IsReady.ShouldBeTrue();
        status.Subscriptions.ShouldBe(1);
        status.IsDegraded.ShouldBeTrue();
        var pending = status.Pending.ShouldHaveSingleItem();
        pending.Key.ShouldBe(Full(Execution("contoso")));
        pending.LastError.ShouldNotBeNull().StatusCode.ShouldBe(503);
        Warnings().ShouldHaveSingleItem().Message.ShouldBe(
            $"Reading the configuration of the subscription {Full(Execution("contoso"))} failed (transient); it stays pending, without an event, and the next poll reads it again. Reading {Full(Execution("contoso"))} failed (503 AccessDenied): No. Ask the owner.");

        // A warning per failed poll.
        await harness.NextPollAsync();
        Warnings().Count.ShouldBe(2);

        _configurations.Heal(Execution("contoso"));
        await harness.NextPollAsync();

        var added = (await watch.WaitForAsync(2))[1];
        added.Kind.ShouldBe(StorytellerChangeKind.Added);
        added.Subscription.Subject.ShouldBe("contoso");
        added.Subscription.Document.Configuration["App:Name"].ShouldBe("contoso");
        harness.Subscriptions.Status.Pending.ShouldBeEmpty();
        harness.Subscriptions.Status.IsDegraded.ShouldBeFalse();
    }

    [Fact]
    public async Task A304_ANewTagWithTheSameData_AndANewVersionOfLabelsAndValuesOnly_AreNoChange_ButTheDocumentStaysCurrent()
    {
        Subscribe("northwind");
        await using var harness = new SubscriptionsHarness(_fixture);
        await using var watch = new WatchProbe(harness.Subscriptions);
        await harness.StartAndPollAsync();
        await watch.WaitForAsync(1);
        var tag = _configurations.Tag(Execution("northwind"));

        await harness.NextPollAsync();
        _configurations.Reads(Execution("northwind"))[^1].Headers["If-None-Match"].ShouldBe(tag);
        harness.Subscriptions.Find("northwind")!.Document.ETag.ShouldBe(tag);

        var newTag = _configurations.PutApp(Execution("northwind"), "northwind", version: 4);
        await harness.NextPollAsync();
        var current = harness.Subscriptions.Find("northwind")!.Document;
        current.ETag.ShouldBe(newTag);
        current.Version.ShouldBe(4);

        _configurations.Put(Execution("northwind"), """{ "App": { "Name": "northwind", "Port": 80 } }""", version: 5, labels: ["blue"], values: new JObject { ["tier"] = "gold" });
        await harness.NextPollAsync();
        current = harness.Subscriptions.Find("northwind")!.Document;
        current.Version.ShouldBe(5);
        current.Labels.ShouldBe(["blue"]);

        // The next event is the first change of the data, so nothing came before it.
        _configurations.PutApp(Execution("northwind"), "renamed", version: 6);
        await harness.NextPollAsync();
        var changes = await watch.WaitForAsync(2);
        changes[1].Kind.ShouldBe(StorytellerChangeKind.Changed);
        changes[1].Subscription.Document.Configuration["App:Name"].ShouldBe("renamed");
        changes[1].Subscription.Document.Version.ShouldBe(6);
        harness.Subscriptions.Find("northwind")!.Document.Configuration["App:Name"].ShouldBe("renamed");
    }

    [Fact]
    public async Task AChangeOfTheExecution_OrOfTheUsage_IsChanged_WithTheSameDocument()
    {
        Subscribe("northwind");
        await using var harness = new SubscriptionsHarness(_fixture);
        await using var watch = new WatchProbe(harness.Subscriptions);
        await harness.StartAndPollAsync();
        var added = (await watch.WaitForAsync(1))[0].Subscription;

        _catalog.Update(Execution("northwind"), json => json["Title"] = "After");
        await harness.NextPollAsync();
        var execution = (await watch.WaitForAsync(2))[1];

        _catalog.Update(Usage("northwind"), json => json["Values"] = new JObject { ["Plan"] = "platinum" });
        await harness.NextPollAsync();
        var usage = (await watch.WaitForAsync(3))[2];

        execution.Kind.ShouldBe(StorytellerChangeKind.Changed);
        execution.Subscription.Execution.Title.ShouldBe("After");
        execution.Subscription.Usage.ShouldBeSameAs(added.Usage);
        execution.Subscription.Document.ETag.ShouldBe(added.Document.ETag);
        usage.Kind.ShouldBe(StorytellerChangeKind.Changed);
        usage.Subscription.Usage.Values["Plan"].ShouldBe("platinum");
        usage.Subscription.Execution.Title.ShouldBe("After");
        harness.Subscriptions.Find("northwind")!.Usage.Values["Plan"].ShouldBe("platinum");
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.UnprocessableEntity, false)]
    public async Task AFailedRead_KeepsTheLastGoodDocument_AndSetsLastError_AndAGoodReadClearsIt(HttpStatusCode status, bool isTransient)
    {
        Subscribe("northwind");
        await using var harness = new SubscriptionsHarness(_fixture);
        await using var watch = new WatchProbe(harness.Subscriptions);
        await harness.StartAndPollAsync();
        var readAt = Now;
        await watch.WaitForAsync(1);

        _configurations.Fail(Execution("northwind"), status, TestAnnotations.ErrorJson);
        await harness.NextPollAsync();

        var failing = harness.Subscriptions.Find("northwind").ShouldNotBeNull();
        failing.Document.Configuration["App:Name"].ShouldBe("northwind");
        failing.LastError.ShouldNotBeNull().StatusCode.ShouldBe((int)status);
        failing.LastError.IsTransient.ShouldBe(isTransient);
        failing.LastErrorAt.ShouldBe(Now);
        failing.LastSucceededAt.ShouldBe(readAt);
        failing.ToString().ShouldEndWith("(northwind, failing)");
        harness.Subscriptions.Status.Failing.ShouldBe(1);
        harness.Subscriptions.Status.Pending.ShouldBeEmpty();
        harness.Subscriptions.Status.IsDegraded.ShouldBeTrue();
        Warnings().ShouldHaveSingleItem().Message.ShouldStartWith(
            $"Reading the configuration of the subscription {Full(Execution("northwind"))} failed ({(isTransient ? "transient" : "definitive")}); it keeps its last good document, and the next poll reads it again.");

        _configurations.Heal(Execution("northwind"));
        await harness.NextPollAsync();

        var healed = harness.Subscriptions.Find("northwind").ShouldNotBeNull();
        healed.LastError.ShouldBeNull();
        healed.LastErrorAt.ShouldBeNull();
        healed.LastSucceededAt.ShouldBe(Now);
        harness.Subscriptions.Status.Failing.ShouldBe(0);
        harness.Subscriptions.Status.IsDegraded.ShouldBeFalse();

        // Neither the failure nor the read after it was an event.
        _configurations.PutApp(Execution("northwind"), "renamed");
        await harness.NextPollAsync();
        (await watch.WaitForAsync(2))[1].Subscription.Document.Configuration["App:Name"].ShouldBe("renamed");
    }

    [Fact]
    public async Task AChangeOfTheExecution_WhileTheReadFails_IsChanged_WithTheLastGoodDocumentAndTheError()
    {
        Subscribe("northwind");
        await using var harness = new SubscriptionsHarness(_fixture);
        await using var watch = new WatchProbe(harness.Subscriptions);
        await harness.StartAndPollAsync();
        await watch.WaitForAsync(1);

        _catalog.Update(Execution("northwind"), json => json["Title"] = "After");
        _configurations.Fail(Execution("northwind"), HttpStatusCode.ServiceUnavailable);
        await harness.NextPollAsync();

        var change = (await watch.WaitForAsync(2))[1];
        change.Kind.ShouldBe(StorytellerChangeKind.Changed);
        change.Subscription.Execution.Title.ShouldBe("After");
        change.Subscription.Document.Configuration["App:Name"].ShouldBe("northwind");
        change.Subscription.LastError.ShouldNotBeNull().StatusCode.ShouldBe(503);
    }

    [Fact]
    public async Task ARemovedSubscription_IsRemoved_WithItsRemovalAndLastDocument_AndItsEntryIsEvicted()
    {
        Subscribe("northwind");
        Subscribe("contoso");
        await using var harness = new SubscriptionsHarness(_fixture);
        await using var watch = new WatchProbe(harness.Subscriptions);
        await harness.StartAndPollAsync();
        await watch.WaitForAsync(2);
        harness.Service.Documents.Cache.Count.ShouldBe(2);

        _catalog.Purge(Execution("northwind"));
        await harness.NextPollAsync();

        var removed = (await watch.WaitForAsync(3))[2];
        removed.Kind.ShouldBe(StorytellerChangeKind.Removed);
        removed.Removal.ShouldBe(new StorytellerRemoval(Execution("northwind"), StorytellerRemovalReason.Missing));
        removed.Subscription.Subject.ShouldBe("northwind");
        removed.Subscription.Document.Configuration["App:Name"].ShouldBe("northwind");
        harness.Subscriptions.Find("northwind").ShouldBeNull();
        harness.Subscriptions.GetAll().ShouldHaveSingleItem().Subject.ShouldBe("contoso");
        harness.Subscriptions.Status.Subscriptions.ShouldBe(1);
        harness.Service.Documents.Cache.Count.ShouldBe(1);

        // Back again: its first read is a full one.
        _catalog.Subscribe("northwind");
        await harness.NextPollAsync();
        (await watch.WaitForAsync(4))[3].Kind.ShouldBe(StorytellerChangeKind.Added);
        _configurations.Reads(Execution("northwind"))[^1].Headers.ShouldNotContainKey("If-None-Match");
    }

    [Fact]
    public async Task APendingSubscriptionThatIsRemoved_RaisesNothing()
    {
        Subscribe("northwind");
        _configurations.Fail(Execution("northwind"), HttpStatusCode.ServiceUnavailable);
        await using var harness = new SubscriptionsHarness(_fixture);
        await using var watch = new WatchProbe(harness.Subscriptions);
        await harness.StartAndPollAsync();
        harness.Subscriptions.Status.Pending.ShouldHaveSingleItem();

        _catalog.Update(Execution("northwind"), json => json["IsDisabled"] = true);
        Subscribe("contoso");
        await harness.NextPollAsync();

        harness.Subscriptions.Status.Pending.ShouldBeEmpty();
        var change = (await watch.WaitForAsync(1)).ShouldHaveSingleItem();
        change.Kind.ShouldBe(StorytellerChangeKind.Added);
        change.Subscription.Subject.ShouldBe("contoso");
    }

    [Fact]
    public async Task WaitUntilReady_Completes_AfterTheFirstPollAndItsReads_ThoughATenantFails()
    {
        Subscribe("northwind");
        _configurations.Fail(Execution("northwind"), HttpStatusCode.Forbidden, TestAnnotations.ErrorJson);
        await using var harness = new SubscriptionsHarness(_fixture);
        var ready = harness.Subscriptions.WaitUntilReadyAsync();
        ready.IsCompleted.ShouldBeFalse();
        harness.Subscriptions.Status.IsReady.ShouldBeFalse();

        await harness.StartAsync();
        await ready.WaitAsync(HostingFixture.Patience);

        harness.Subscriptions.Status.IsReady.ShouldBeTrue();
        harness.Subscriptions.Status.Pending.ShouldHaveSingleItem().LastError!.StatusCode.ShouldBe(403);
        await harness.Subscriptions.WaitUntilReadyAsync();
    }

    [Fact]
    public async Task WaitUntilReady_ThrowsADefinitiveFailureOfThePoll_WhileNeverReady()
    {
        Subscribe("northwind");
        _catalog.FailPage("executions", null, HttpStatusCode.Forbidden, TestAnnotations.ErrorJson);
        await using var harness = new SubscriptionsHarness(_fixture);
        var ready = harness.Subscriptions.WaitUntilReadyAsync();

        await harness.StartAndPollAsync();

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => ready.WaitAsync(HostingFixture.Patience));
        exception.StatusCode.ShouldBe(403);
        exception.IsTransient.ShouldBeFalse();
        exception.Message.ShouldContain("needs Annotation.Read");
        var status = harness.Subscriptions.Status;
        status.IsReady.ShouldBeFalse();
        status.LastPollError.ShouldNotBeNull().StatusCode.ShouldBe(403);
        status.ConsecutivePollFailures.ShouldBe(1);
        status.IsDegraded.ShouldBeTrue();

        // The loop keeps polling.
        _catalog.Heal();
        await harness.NextPollAsync();
        await harness.Subscriptions.WaitUntilReadyAsync().WaitAsync(HostingFixture.Patience);
        harness.Subscriptions.Status.ConsecutivePollFailures.ShouldBe(0);
        harness.Subscriptions.Status.LastPollError.ShouldBeNull();
    }

    [Fact]
    public async Task WaitUntilReady_WaitsThroughATransientFailureOfThePoll()
    {
        Subscribe("northwind");
        _catalog.FailPage("usages", null, HttpStatusCode.ServiceUnavailable);
        await using var harness = new SubscriptionsHarness(_fixture);
        await harness.StartAndPollAsync();
        var ready = harness.Subscriptions.WaitUntilReadyAsync();

        await Task.Delay(50);
        ready.IsCompleted.ShouldBeFalse();
        harness.Subscriptions.Status.LastPollError.ShouldNotBeNull().IsTransient.ShouldBeTrue();

        _catalog.Heal();
        await harness.NextPollAsync();
        await ready.WaitAsync(HostingFixture.Patience);
        harness.Subscriptions.Find("northwind").ShouldNotBeNull();
    }

    [Fact]
    public async Task WaitUntilReadyOnStart_TheStartWaitsForTheFirstPoll()
    {
        Subscribe("northwind");
        await using var harness = new SubscriptionsHarness(_fixture, options => options.WaitUntilReadyOnStart = true);

        await harness.StartAsync();

        harness.Subscriptions.Status.IsReady.ShouldBeTrue();
        harness.Subscriptions.Find("northwind").ShouldNotBeNull();
    }

    [Fact]
    public async Task TheHost_StartsTheSubscriptions_AndWaitsForThemWithWaitUntilReadyOnStart()
    {
        Subscribe("northwind");
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddStoryteller(_fixture.Configure()).AddSubscriptions("prod", options => options.WaitUntilReadyOnStart = true);
        using var host = _fixture.Track(builder.Build());

        await host.StartAsync().WaitAsync(HostingFixture.Patience);

        var subscriptions = host.Services.GetRequiredService<IStorytellerSubscriptions>();
        subscriptions.Status.IsReady.ShouldBeTrue();
        subscriptions.Find("northwind").ShouldNotBeNull().Document.Configuration["App:Name"].ShouldBe("northwind");
        await host.StopAsync().WaitAsync(HostingFixture.Patience);
        (await ToListAsync(subscriptions.WatchAsync())).ShouldHaveSingleItem().Kind.ShouldBe(StorytellerChangeKind.Added);
    }

    [Fact]
    public async Task WaitUntilReadyOnStart_FailsTheStart_AfterStartTimeout()
    {
        Subscribe("northwind");
        var gate = new GatedResponse(() => StubHttpHandler.Response(HttpStatusCode.OK, TestAnnotations.Json(FakeCatalog.ResponsibilityKey)));
        _fixture.Http.Respond(gate.RespondAsync);
        await using var harness = new SubscriptionsHarness(_fixture, options =>
        {
            options.WaitUntilReadyOnStart = true;
            options.StartTimeout = TimeSpan.FromMilliseconds(200);
        });

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => harness.StartAsync().WaitAsync(HostingFixture.Patience));

        exception.Message.ShouldBe(
            "The subscriptions of @acme.billing.default.rst.invoicing in the context prod were not ready within the StartTimeout of 00:00:00.2000000: the first poll and the reads of its documents did not complete.");
        exception.Key.ShouldBe("@acme.billing.default.rst.invoicing");
        exception.IsTransient.ShouldBeTrue();
        gate.Release();
    }

    [Fact]
    public async Task WaitUntilReadyOnStart_FailsTheStart_WithADefinitiveFailureOfThePoll()
    {
        Subscribe("northwind");
        _catalog.FailPage("executions", null, HttpStatusCode.Unauthorized);
        await using var harness = new SubscriptionsHarness(_fixture, options => options.WaitUntilReadyOnStart = true);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => harness.StartAsync().WaitAsync(HostingFixture.Patience));

        exception.StatusCode.ShouldBe(401);
        exception.IsTransient.ShouldBeFalse();
    }

    [Fact]
    public async Task Watch_ReplaysTheCurrentSubscriptions_ThenStreamsTheChanges_ForTwoWatchers()
    {
        Subscribe("northwind");
        await using var harness = new SubscriptionsHarness(_fixture);
        await harness.StartAndPollAsync();
        await using var first = new WatchProbe(harness.Subscriptions);
        (await first.WaitForAsync(1))[0].Subscription.Subject.ShouldBe("northwind");

        Subscribe("contoso");
        await harness.NextPollAsync();
        (await first.WaitForAsync(2))[1].Subscription.Subject.ShouldBe("contoso");

        await using var second = new WatchProbe(harness.Subscriptions);
        (await second.WaitForAsync(2)).Select(change => (change.Kind, change.Subscription.Subject)).ShouldBe(
            [(StorytellerChangeKind.Added, "contoso"), (StorytellerChangeKind.Added, "northwind")]);

        _configurations.PutApp(Execution("northwind"), "renamed");
        _catalog.Purge(Usage("contoso"));
        await harness.NextPollAsync();

        (await first.WaitForAsync(4)).Skip(2).Select(change => (change.Kind, change.Subscription.Subject)).ShouldBe(
            [(StorytellerChangeKind.Removed, "contoso"), (StorytellerChangeKind.Changed, "northwind")]);
        (await second.WaitForAsync(4)).Skip(2).Select(change => (change.Kind, change.Subscription.Subject)).ShouldBe(
            [(StorytellerChangeKind.Removed, "contoso"), (StorytellerChangeKind.Changed, "northwind")]);
    }

    [Fact]
    public async Task Watch_EndsWithOperationCanceledException_WhenItsTokenIsCancelled()
    {
        Subscribe("northwind");
        await using var harness = new SubscriptionsHarness(_fixture);
        await harness.StartAndPollAsync();
        await using var watch = new WatchProbe(harness.Subscriptions);
        await watch.WaitForAsync(1);

        watch.Stop();

        await Should.ThrowAsync<OperationCanceledException>(() => watch.Completion);
    }

    [Fact]
    public async Task Stopping_EndsTheWatchers_DisposesThePrivateDocuments_AndKeepsTheSubscriptions()
    {
        Subscribe("northwind");
        var harness = new SubscriptionsHarness(_fixture);
        var documents = harness.Services.GetRequiredService<IStorytellerDocuments>();
        await harness.StartAndPollAsync();
        await using var watch = new WatchProbe(harness.Subscriptions);
        await watch.WaitForAsync(1);

        await harness.DisposeAsync();

        await watch.Completion.WaitAsync(HostingFixture.Patience);
        Should.Throw<ObjectDisposedException>(() => harness.Service.Documents.GetEntryAsync(FullKey.Parse(Full(Execution("northwind"))), StorytellerReadMode.Resolved, CancellationToken.None));
        harness.Subscriptions.Find("northwind").ShouldNotBeNull().Document.Configuration["App:Name"].ShouldBe("northwind");
        (await ToListAsync(harness.Subscriptions.WatchAsync())).ShouldHaveSingleItem().Kind.ShouldBe(StorytellerChangeKind.Added);

        // The documents of the subscriptions are their own: the application's still read, and their cache never held
        // what the subscriptions read.
        harness.Service.Documents.ShouldNotBeSameAs(documents);
        (await documents.GetAsync(Full(Execution("northwind")))).Configuration["App:Name"].ShouldBe("northwind");
        _configurations.Reads(Execution("northwind"))[^1].Headers.ShouldNotContainKey("If-None-Match");
    }

    [Fact]
    public async Task ALoopThatBreaks_KeepsTheSubscriptions_ReportsItsFailureInTheStatus_AndEndsTheWatchersWithIt()
    {
        Subscribe("northwind");
        BreakingSource? breaking = null;
        await using var harness = new SubscriptionsHarness(_fixture, source: inner => breaking = new BreakingSource(inner));
        await harness.StartAndPollAsync();
        await using var watch = new WatchProbe(harness.Subscriptions);
        await watch.WaitForAsync(1);

        breaking!.Break();
        _fixture.Time.Advance(SubscriptionsHarness.Interval);
        await harness.Service.ExecuteTask!.WaitAsync(HostingFixture.Patience);

        const string message = "The informer of the subscriptions of @acme.billing.default.rst.invoicing in the context prod stopped: it failed with InvalidOperationException.";
        var failure = await Should.ThrowAsync<StorytellerConfigurationException>(() => watch.Completion.WaitAsync(HostingFixture.Patience));
        failure.Message.ShouldBe(message);
        failure.IsTransient.ShouldBeFalse();
        var status = harness.Subscriptions.Status;
        status.IsReady.ShouldBeTrue();
        status.LastPollError.ShouldNotBeNull().Message.ShouldBe(message);
        status.ConsecutivePollFailures.ShouldBe(1);
        status.IsDegraded.ShouldBeTrue();
        harness.Subscriptions.Find("northwind").ShouldNotBeNull().Document.Configuration["App:Name"].ShouldBe("northwind");
        await harness.Subscriptions.WaitUntilReadyAsync().WaitAsync(HostingFixture.Patience);

        // A watch that starts after it replays what is held and ends with the failure as well.
        var late = new List<StorytellerSubscriptionChange>();
        (await Should.ThrowAsync<StorytellerConfigurationException>(async () =>
        {
            await foreach (var change in harness.Subscriptions.WatchAsync())
            {
                late.Add(change);
            }
        })).Message.ShouldBe(message);
        late.ShouldHaveSingleItem().Kind.ShouldBe(StorytellerChangeKind.Added);

        _fixture.Logs.Entries.Where(entry => entry.Level == LogLevel.Error).ShouldHaveSingleItem().Message.ShouldBe(
            "The informer of the subscriptions of @acme.billing.default.rst.invoicing in the context prod stopped: it failed with InvalidOperationException. The subscriptions keep what they held, the status reports the failure and the watchers end with it.");
        _fixture.Logs.Entries.ShouldAllBe(entry => !entry.Message.Contains("S3cr3t"));
        status.ToString().ShouldNotContain("S3cr3t");
    }

    [Fact]
    public async Task AStopBeforeTheSubscriptionsWereReady_EndsTheWait_WithOperationCanceledException()
    {
        Subscribe("northwind");
        _catalog.FailPage("executions", null, HttpStatusCode.ServiceUnavailable);
        var harness = new SubscriptionsHarness(_fixture);
        await harness.StartAndPollAsync();
        var ready = harness.Subscriptions.WaitUntilReadyAsync();

        await harness.DisposeAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => ready.WaitAsync(HostingFixture.Patience));
    }

    [Fact]
    public async Task AZeroInterval_PollsOnce_AndThenEndsTheWatchers()
    {
        Subscribe("northwind");
        await using var harness = new SubscriptionsHarness(_fixture, options => options.PollInterval = TimeSpan.Zero);

        await harness.StartAsync();
        await harness.Subscriptions.WaitUntilReadyAsync().WaitAsync(HostingFixture.Patience);
        await harness.Service.ExecuteTask!.WaitAsync(HostingFixture.Patience);

        (await ToListAsync(harness.Subscriptions.WatchAsync())).ShouldHaveSingleItem().Subscription.Subject.ShouldBe("northwind");
        _catalog.Lists("executions").Count.ShouldBe(1);
        _fixture.Time.Created.ShouldBe(0);
    }

    [Fact]
    public async Task AZeroInterval_WhoseOnePollFailed_ThrowsItsFailureFromWaitUntilReady()
    {
        _catalog.FailPage("executions", null, HttpStatusCode.ServiceUnavailable);
        await using var harness = new SubscriptionsHarness(_fixture, options => options.PollInterval = TimeSpan.Zero);

        await harness.StartAsync();

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => harness.Subscriptions.WaitUntilReadyAsync().WaitAsync(HostingFixture.Patience));
        exception.StatusCode.ShouldBe(503);
        exception.IsTransient.ShouldBeTrue();
    }

    [Fact]
    public async Task ReadsRun_AtMostMaxConcurrentReadsAtOnce()
    {
        foreach (var subject in Enumerable.Range(1, 10).Select(number => $"s{number}"))
        {
            Subscribe(subject);
        }

        _catalog.HoldReads();
        _configurations.Hold();
        await using var harness = new SubscriptionsHarness(_fixture, options => options.MaxConcurrentReads = 2);

        var polled = harness.StartAndPollAsync();

        // The annotations of the informer, then the documents of the loop.
        await OverlapAsync(() => _catalog.HeldReads, _catalog.ReleaseReads);
        await OverlapAsync(() => _configurations.Held, _configurations.Release);
        await polled;

        harness.Subscriptions.GetAll().Count.ShouldBe(10);
        _catalog.MaxInFlight.ShouldBe(2);
        _configurations.MaxInFlight.ShouldBe(2);
    }

    [Fact]
    public async Task Document_IsANewInstancePerAccess()
    {
        Subscribe("northwind");
        await using var harness = new SubscriptionsHarness(_fixture);
        await harness.StartAndPollAsync();
        var subscription = harness.Subscriptions.Find("northwind")!;

        var first = subscription.Document;
        var second = subscription.Document;
        first.Content["App"]!["Name"] = "changed";

        first.ShouldNotBeSameAs(second);
        second.Content["App"]!["Name"]!.Value<string>().ShouldBe("northwind");
        subscription.Document.Configuration["App:Name"].ShouldBe("northwind");
        harness.Subscriptions.Find("northwind")!.Document.Content["App"]!["Name"]!.Value<string>().ShouldBe("northwind");
    }

    [Fact]
    public async Task ASecretThatRotatedOnA304_IsChanged_AndNoValueReachesTheLogsOrTheStatus()
    {
        const string reference = "kv://vault/db-password";
        var resolver = new TestResolver { Secrets = { [reference] = "first-S3cr3t" } };
        _catalog.Subscribe("northwind");
        _configurations.Put(Execution("northwind"), $$"""{ "Db": { "Password": "{{reference}}" } }""");
        await using var harness = new SubscriptionsHarness(_fixture, options: options =>
        {
            options.ReferenceResolver = resolver;
            options.SecretRefreshInterval = TimeSpan.FromSeconds(45);
        });
        await using var watch = new WatchProbe(harness.Subscriptions);
        await harness.StartAndPollAsync();
        (await watch.WaitForAsync(1))[0].Subscription.Document.Configuration["Db:Password"].ShouldBe("first-S3cr3t");
        resolver.Secrets[reference] = "second-S3cr3t";

        // 30 s: the resolved value is fresh. 60 s: it is resolved again, on a 304.
        await harness.NextPollAsync();
        await harness.NextPollAsync();

        var change = (await watch.WaitForAsync(2))[1];
        change.Kind.ShouldBe(StorytellerChangeKind.Changed);
        change.Subscription.Document.Configuration["Db:Password"].ShouldBe("second-S3cr3t");
        _configurations.Reads(Execution("northwind"))[^1].Headers.ShouldContainKey("If-None-Match");
        _fixture.Logs.Entries.ShouldAllBe(entry => !entry.Message.Contains("S3cr3t"));
        harness.Subscriptions.Status.ToString().ShouldNotContain("S3cr3t");
        change.ToString().ShouldNotContain("S3cr3t");
    }

    [Fact]
    public void TheStatus_PrintsErrorsByTheirMessages()
    {
        var error = new StorytellerConfigurationException("Reading failed (503).", new InvalidOperationException("inner body"));
        var status = new StorytellerSubscriptionsStatus
        {
            LastPollError = error,
            ConsecutivePollFailures = 1,
            Pending = [new StorytellerPendingSubscription("@acme.billing.default.exe.contoso.invoicing.prod", error)],
        };

        status.IsDegraded.ShouldBeTrue();
        status.ToString().ShouldContain("LastPollError = Reading failed (503).");
        status.ToString().ShouldContain("Pending = [@acme.billing.default.exe.contoso.invoicing.prod]");
        status.ToString().ShouldNotContain("inner body");
        status.Pending[0].ToString().ShouldBe("StorytellerPendingSubscription { Key = @acme.billing.default.exe.contoso.invoicing.prod, LastError = Reading failed (503). }");
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }

    private static string Execution(string subject)
    {
        return FakeCatalog.ExecutionKey(subject);
    }

    private static string Usage(string subject)
    {
        return FakeCatalog.UsageKey(subject);
    }

    private static string Full(string annotationKey)
    {
        return FakeCatalog.Full(annotationKey);
    }

    // Waits until two answers are held, the limit of the test, gives more a moment to arrive, and releases them.
    private static async Task OverlapAsync(Func<int> held, Action release)
    {
        var waiting = Stopwatch.StartNew();

        while (held() < 2)
        {
            if (waiting.Elapsed > HostingFixture.Patience)
            {
                throw new TimeoutException($"{held()} reads arrived at once.");
            }

            await Task.Delay(5);
        }

        await Task.Delay(50);
        release();
    }

    private static async Task<IReadOnlyList<T>> ToListAsync<T>(IAsyncEnumerable<T> items)
    {
        var list = new List<T>();

        await foreach (var item in items)
        {
            list.Add(item);
        }

        return list;
    }

    // A subscription of the subject in the catalog, and the document of its execution.
    private void Subscribe(string subject)
    {
        _catalog.Subscribe(subject);
        _configurations.PutApp(Execution(subject), subject);
    }

    private IReadOnlyList<RecordingLoggerFactory.LogEntry> Warnings()
    {
        return _fixture.Logs.Entries.Where(entry => entry.Category == LogCategory && entry.Level >= LogLevel.Warning).ToList();
    }
}
