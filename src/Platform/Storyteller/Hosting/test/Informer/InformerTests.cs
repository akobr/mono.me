using System.Net;
using _42.Platform.Storyteller.Sdk.Configuring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Informer;

public sealed class InformerTests : IDisposable
{
    private const string LogCategory = "_42.Platform.Storyteller.Sdk.Hosting.Informer";

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly HostingFixture _fixture = new();
    private readonly FakeCatalog _catalog;

    public InformerTests()
    {
        _catalog = new FakeCatalog(_fixture);
    }

    private DateTimeOffset Now => _fixture.Time.GetUtcNow();

    [Fact]
    public async Task TheFirstPoll_AddsEverySubscription_WithSnapshotsOfPointReads()
    {
        _catalog.Subscribe(
            "northwind",
            execution: json => json["Values"] = new JObject { ["Region"] = "eu" },
            usage: json =>
            {
                json["Values"] = new JObject { ["Plan"] = "gold", ["Seats"] = 5 };
                json["Labels"] = new JArray("trial");
            });
        _catalog.Subscribe("contoso");

        var changes = await WatchOnceAsync();

        changes.Select(change => change.Kind).ShouldBe([StorytellerChangeKind.Added, StorytellerChangeKind.Added]);
        changes.Select(change => change.Execution.Key).ShouldBe([Full(Execution("contoso")), Full(Execution("northwind"))]);
        var northwind = changes[1];
        northwind.Removal.ShouldBeNull();
        northwind.Execution.SubjectName.ShouldBe("northwind");
        northwind.Execution.ResponsibilityName.ShouldBe(FakeCatalog.Responsibility);
        northwind.Execution.ContextName.ShouldBe(FakeCatalog.Context);
        northwind.Execution.Values["Region"].ShouldBe("eu");
        northwind.Usage.Key.ShouldBe(Full(Usage("northwind")));
        northwind.Usage.Values["Plan"].ShouldBe("gold");
        northwind.Usage.Labels.ShouldBe(["trial"]);
        _fixture.Logs.Entries.ShouldAllBe(entry => !entry.Message.Contains("gold"));
    }

    [Fact]
    public async Task TheFirstPoll_ReadsTheResponsibility_TheTwoListsAndThePointReads_WithTheirFlags()
    {
        _catalog.Subscribe("northwind");

        await WatchOnceAsync();

        var requests = _fixture.AnnotationRequests;
        requests[0].Uri.AbsolutePath.ShouldBe(HostingFixture.AnnotationPath(FakeCatalog.ResponsibilityKey));
        requests[0].Uri.Query.ShouldBe("?includeInactive=true");
        requests.Skip(1).Take(2).Select(request => request.Uri.AbsolutePath).ShouldBe([FakeCatalog.ListPath("executions"), FakeCatalog.ListPath("usages")], ignoreOrder: true);
        requests.Skip(1).Take(2).ShouldAllBe(request => request.Uri.Query == string.Empty);
        requests.Skip(3).Select(request => request.Uri.AbsolutePath).ShouldBe([HostingFixture.AnnotationPath(Execution("northwind")), HostingFixture.AnnotationPath(Usage("northwind"))], ignoreOrder: true);
        requests.Skip(3).ShouldAllBe(request => request.Uri.Query == "?includeInactive=true");
        requests.Count.ShouldBe(5);
    }

    [Fact]
    public async Task OnlyTheExecutionsOfTheContext_AreSubscriptions()
    {
        _catalog.Subscribe("northwind");
        _catalog.Put(Execution("northwind", "dev"));
        _catalog.Subscribe("contoso", "dev");

        var change = (await WatchOnceAsync()).ShouldHaveSingleItem();

        change.Execution.Key.ShouldBe(Full(Execution("northwind")));
        _catalog.Reads(Execution("northwind", "dev")).ShouldBeEmpty();
    }

    [Fact]
    public async Task AnExecutionWithoutAnActiveUsage_IsNoSubscription()
    {
        _catalog.Subscribe("northwind", usage: json => json["IsDisabled"] = true);
        _catalog.Put(Execution("contoso"));

        (await WatchOnceAsync()).ShouldBeEmpty();

        _catalog.Reads(Execution("northwind")).ShouldBeEmpty();
        _catalog.Reads(Execution("contoso")).ShouldBeEmpty();
    }

    [Fact]
    public async Task ANewModifiedOfTheExecution_IsChanged_AndOnlyTheExecutionIsReadAgain()
    {
        _catalog.Subscribe("northwind", execution: json => json["Title"] = "Before");
        _catalog.Subscribe("contoso");
        await using var probe = Watch();
        (await probe.FirstPollAsync()).Count.ShouldBe(2);

        _catalog.Update(Execution("northwind"), json => json["Title"] = "After");
        var change = (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem();

        change.Kind.ShouldBe(StorytellerChangeKind.Changed);
        change.Execution.Title.ShouldBe("After");
        change.Usage.Key.ShouldBe(Full(Usage("northwind")));
        _catalog.Reads(Execution("northwind")).Count.ShouldBe(2);
        _catalog.Reads(Usage("northwind")).Count.ShouldBe(1);
        _catalog.Reads(Execution("contoso")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task ANewModifiedOfTheUsage_IsChanged_AndOnlyTheUsageIsReadAgain()
    {
        _catalog.Subscribe("northwind", usage: json => json["Values"] = new JObject { ["Plan"] = "gold" });
        await using var probe = Watch();
        await probe.FirstPollAsync();

        _catalog.Update(Usage("northwind"), json => json["Values"] = new JObject { ["Plan"] = "platinum" });
        var change = (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem();

        change.Kind.ShouldBe(StorytellerChangeKind.Changed);
        change.Usage.Values["Plan"].ShouldBe("platinum");
        _catalog.Reads(Execution("northwind")).Count.ShouldBe(1);
        _catalog.Reads(Usage("northwind")).Count.ShouldBe(2);
    }

    [Fact]
    public async Task WithoutModified_AChangeIsToldByTheHashOfTheListItem()
    {
        _catalog.Subscribe("northwind", stamp: false);
        await using var probe = Watch();
        await probe.FirstPollAsync();

        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();
        _catalog.Reads(Execution("northwind")).Count.ShouldBe(1);

        _catalog.Update(Execution("northwind"), json => json["Labels"] = new JArray("blue"), stamp: false);
        var change = (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem();

        change.Kind.ShouldBe(StorytellerChangeKind.Changed);
        change.Execution.Labels.ShouldBe(["blue"]);
    }

    [Fact]
    public async Task WithoutAChange_APoll_ReadsTheListsOnly()
    {
        _catalog.Subscribe("northwind");
        await using var probe = Watch();
        await probe.FirstPollAsync();
        var requests = _fixture.AnnotationRequests.Count;

        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();

        _fixture.AnnotationRequests.Skip(requests).Select(request => request.Uri.AbsolutePath).ShouldBe(
            [FakeCatalog.ListPath("executions"), FakeCatalog.ListPath("usages")],
            ignoreOrder: true);
    }

    [Theory]
    [InlineData(true, StorytellerRemovalReason.Deleted)]
    [InlineData(true, StorytellerRemovalReason.Disabled)]
    [InlineData(true, StorytellerRemovalReason.Scheduled)]
    [InlineData(true, StorytellerRemovalReason.Expired)]
    [InlineData(true, StorytellerRemovalReason.Missing)]
    [InlineData(false, StorytellerRemovalReason.Deleted)]
    [InlineData(false, StorytellerRemovalReason.Disabled)]
    [InlineData(false, StorytellerRemovalReason.Scheduled)]
    [InlineData(false, StorytellerRemovalReason.Expired)]
    [InlineData(false, StorytellerRemovalReason.Missing)]
    public async Task ASubscriptionThatEnds_IsRemoved_WithTheAnnotationThatEndedIt_AndTheSnapshotsHeldBefore(bool byExecution, StorytellerRemovalReason reason)
    {
        var target = byExecution ? Execution("northwind") : Usage("northwind");
        Action<JObject>? expiresSoon = reason == StorytellerRemovalReason.Expired ? json => json["ExpiresAt"] = Now.AddSeconds(10).ToString("O") : null;
        _catalog.Subscribe("northwind", execution: byExecution ? expiresSoon : null, usage: byExecution ? null : expiresSoon);
        await using var probe = Watch();
        var added = (await probe.FirstPollAsync()).ShouldHaveSingleItem();
        added.Kind.ShouldBe(StorytellerChangeKind.Added);

        // Expired: the poll after ExpiresAt passed, without a write.
        switch (reason)
        {
            case StorytellerRemovalReason.Deleted:
                _catalog.Update(target, TestAnnotations.Deleted(), stamp: false);
                break;
            case StorytellerRemovalReason.Disabled:
                _catalog.Update(target, json => json["IsDisabled"] = true);
                break;
            case StorytellerRemovalReason.Scheduled:
                _catalog.Update(target, json => json["ValidFrom"] = Now.AddDays(1).ToString("O"));
                break;
            case StorytellerRemovalReason.Missing:
                _catalog.Purge(target);
                break;
        }

        var change = (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem();

        change.Kind.ShouldBe(StorytellerChangeKind.Removed);
        change.Removal.ShouldBe(new StorytellerRemoval(target, reason));

        // The confirmation reads both again; the snapshots must still be the ones held.
        change.Execution.ShouldBeSameAs(added.Execution);
        change.Usage.ShouldBeSameAs(added.Usage);
        _catalog.Reads(target)[^1].Uri.Query.ShouldBe("?includeInactive=true&includeDeleted=true");
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenBothEnded_TheRemovalNamesTheExecution()
    {
        _catalog.Subscribe("northwind");
        await using var probe = Watch();
        await probe.FirstPollAsync();

        _catalog.Update(Usage("northwind"), TestAnnotations.Deleted(), stamp: false);
        _catalog.Update(Execution("northwind"), json => json["IsDisabled"] = true);

        (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem().Removal.ShouldBe(
            new StorytellerRemoval(Execution("northwind"), StorytellerRemovalReason.Disabled));
    }

    [Fact]
    public async Task ARemovalThatAnActiveReadRefutes_RaisesNothing_AndTheSubscriptionStays()
    {
        _catalog.Subscribe("northwind");
        await using var probe = Watch();
        await probe.FirstPollAsync();

        _catalog.Unlist(Execution("northwind"));
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();
        _fixture.Logs.Entries.ShouldContain(entry => entry.Level == LogLevel.Debug && entry.Message.Contains("was not listed, but its execution and usage read as active"));

        _catalog.Relist(Execution("northwind"));
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();
    }

    [Fact]
    public async Task AFailedPointRead_RaisesNothing_AndTheNextPollReadsAgain()
    {
        _catalog.Subscribe("northwind");
        _catalog.Fail(Execution("northwind"), HttpStatusCode.ServiceUnavailable, TestAnnotations.ErrorJson);
        await using var probe = Watch();

        (await probe.FirstPollAsync()).ShouldBeEmpty();
        var warning = Warnings().ShouldHaveSingleItem();
        warning.Message.ShouldBe(
            $"Reading {Full(Execution("northwind"))} for the subscription {Full(Execution("northwind"))} failed (transient); the subscription stays as it was, and the next poll reads it again. Reading {Full(Execution("northwind"))} failed (503 AccessDenied): No. Ask the owner.");

        _catalog.Heal();
        (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem().Kind.ShouldBe(StorytellerChangeKind.Added);
    }

    // The other annotation of the subscription reads as active, so the answer of the failed read is needed.
    [Theory]
    [InlineData(true, HttpStatusCode.Forbidden, "definitive", "a machine restricted to an annotation reads that annotation and its descendants only")]
    [InlineData(false, HttpStatusCode.Forbidden, "definitive", "a machine restricted to an annotation reads that annotation and its descendants only")]
    [InlineData(false, HttpStatusCode.ServiceUnavailable, "transient", "failed (503)")]
    public async Task AFailedConfirmation_KeepsTheSubscription_UntilAReadTells(bool ofTheExecution, HttpStatusCode status, string failureClass, string error)
    {
        var ended = ofTheExecution ? Execution("northwind") : Usage("northwind");
        _catalog.Subscribe("northwind");
        await using var probe = Watch();
        await probe.FirstPollAsync();

        _catalog.Update(ended, json => json["IsDisabled"] = true);
        _catalog.Fail(ended, status, status == HttpStatusCode.Forbidden ? TestAnnotations.RestrictedJson : null);
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();
        var warning = Warnings().ShouldHaveSingleItem().Message;
        warning.ShouldStartWith($"Reading {Full(ended)} for the subscription {Full(Execution("northwind"))} failed ({failureClass}); the subscription stays as it was");
        warning.ShouldContain(error);

        _catalog.Heal();
        (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem().Removal.ShouldBe(
            new StorytellerRemoval(ended, StorytellerRemovalReason.Disabled));
    }

    [Fact]
    public async Task AnEndedExecution_RemovesTheSubscription_ThoughTheReadOfItsUsageFails()
    {
        _catalog.Subscribe("northwind");
        await using var probe = Watch();
        await probe.FirstPollAsync();

        _catalog.Update(Execution("northwind"), json => json["IsDisabled"] = true);
        _catalog.Fail(Usage("northwind"), HttpStatusCode.ServiceUnavailable);

        (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem().Removal.ShouldBe(
            new StorytellerRemoval(Execution("northwind"), StorytellerRemovalReason.Disabled));
        _catalog.Reads(Usage("northwind")).Count.ShouldBe(2);
        Warnings().ShouldBeEmpty();
    }

    [Fact]
    public async Task ACandidateGoneSinceTheList_IsNotAdded_AndLogsNoWarning()
    {
        _catalog.Subscribe("northwind");
        _catalog.Fail(Usage("northwind"), HttpStatusCode.NotFound);
        await using var probe = Watch();

        (await probe.FirstPollAsync()).ShouldBeEmpty();
        Warnings().ShouldBeEmpty();
        _fixture.Logs.Entries.ShouldContain(entry => entry.Level == LogLevel.Debug
            && entry.Message == $"{Full(Usage("northwind"))} of the subscription {Full(Execution("northwind"))} is gone since it was listed; the next poll diffs it again.");

        _catalog.Heal();
        (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem().Kind.ShouldBe(StorytellerChangeKind.Added);
    }

    [Fact]
    public async Task AChangedAnnotationGoneSinceTheList_RaisesNothing_AndLogsNoWarning()
    {
        _catalog.Subscribe("northwind");
        await using var probe = Watch();
        await probe.FirstPollAsync();

        _catalog.Update(Execution("northwind"), json => json["Title"] = "After");
        _catalog.Fail(Execution("northwind"), HttpStatusCode.NotFound);
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();
        Warnings().ShouldBeEmpty();

        _catalog.Heal();
        var change = (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem();
        change.Kind.ShouldBe(StorytellerChangeKind.Changed);
        change.Execution.Title.ShouldBe("After");
    }

    [Fact]
    public async Task ASubscriptionNeverAdded_IsNeverRemoved()
    {
        _catalog.Subscribe("northwind");
        _catalog.Fail(Usage("northwind"), HttpStatusCode.ServiceUnavailable);
        await using var probe = Watch();
        (await probe.FirstPollAsync()).ShouldBeEmpty();

        _catalog.Purge(Execution("northwind"));
        _catalog.Heal();
        var reads = _catalog.Reads(Execution("northwind")).Count;

        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();
        _catalog.Reads(Execution("northwind")).Count.ShouldBe(reads);
    }

    [Fact]
    public async Task ACandidateReadAsInactive_IsNotAdded_UntilAPollReadsItActive()
    {
        _catalog.Subscribe("northwind");
        _catalog.ReadAs(Usage("northwind"), json => json["ExpiresAt"] = Now.AddSeconds(-1).ToString("O"));
        await using var probe = Watch();

        (await probe.FirstPollAsync()).ShouldBeEmpty();
        Warnings().ShouldBeEmpty();

        _catalog.Heal();
        (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem().Kind.ShouldBe(StorytellerChangeKind.Added);
    }

    [Fact]
    public async Task AChangedSubscriptionReadAsInactive_RaisesNothing_AndIsRemovedWhenTheListDropsIt()
    {
        _catalog.Subscribe("northwind");
        await using var probe = Watch();
        await probe.FirstPollAsync();

        _catalog.Update(Execution("northwind"), json => json["Title"] = "After");
        _catalog.ReadAs(Execution("northwind"), json => json["IsDisabled"] = true);
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();

        _catalog.Heal();
        _catalog.Update(Execution("northwind"), json => json["IsDisabled"] = true);
        (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem().Removal.ShouldBe(
            new StorytellerRemoval(Execution("northwind"), StorytellerRemovalReason.Disabled));
    }

    [Fact]
    public async Task AFailedPoll_EmitsNothing_KeepsTheSubscriptions_AndPollsAgain()
    {
        _catalog.Subscribe("northwind");
        await using var probe = Watch();
        (await probe.FirstPollAsync()).ShouldHaveSingleItem();

        _catalog.FailPage("executions", null, HttpStatusCode.ServiceUnavailable);
        _catalog.Subscribe("contoso");
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();
        Warnings().ShouldHaveSingleItem().Message.ShouldStartWith(
            "Polling the subscriptions of @acme.billing.default.rst.invoicing in the context prod failed (transient); the 1 subscriptions held stay as they are. Listing the executions of @acme.billing.default.rst.invoicing failed (503).");

        _catalog.Heal();
        var change = (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem();
        change.Kind.ShouldBe(StorytellerChangeKind.Added);
        change.Execution.SubjectName.ShouldBe("contoso");
    }

    [Fact]
    public async Task TheChangesOfAPoll_AreRemovedFirst_ThenChanged_ThenAdded_EachByKey()
    {
        // The lists answer in descending key order, and b1 is held before a1, so only a sort puts each kind in key order.
        foreach (var subject in new[] { "b1", "c1", "c2" })
        {
            _catalog.Subscribe(subject);
        }

        await using var probe = Watch();
        (await probe.FirstPollAsync()).Count.ShouldBe(3);
        _catalog.Subscribe("a1");
        (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem().Execution.SubjectName.ShouldBe("a1");

        _catalog.Subscribe("z2");
        _catalog.Update(Execution("c2"), json => json["Title"] = "Changed");
        _catalog.Purge(Execution("a1"));
        _catalog.Subscribe("z1");
        _catalog.Update(Usage("c1"), json => json["Title"] = "Changed");
        _catalog.Purge(Usage("b1"));

        var changes = await probe.NextPollAsync(Interval);

        changes.Select(change => (change.Kind, change.Execution.SubjectName)).ShouldBe(
        [
            (StorytellerChangeKind.Removed, "a1"),
            (StorytellerChangeKind.Removed, "b1"),
            (StorytellerChangeKind.Changed, "c1"),
            (StorytellerChangeKind.Changed, "c2"),
            (StorytellerChangeKind.Added, "z1"),
            (StorytellerChangeKind.Added, "z2"),
        ]);
        changes[0].Removal.ShouldBe(new StorytellerRemoval(Execution("a1"), StorytellerRemovalReason.Missing));
        changes[1].Removal.ShouldBe(new StorytellerRemoval(Usage("b1"), StorytellerRemovalReason.Missing));
    }

    [Fact]
    public async Task AResponsibilityThatDoesNotExist_FailsThePoll_AsDefinitive()
    {
        _catalog.Purge(FakeCatalog.ResponsibilityKey);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => WatchOnceAsync());

        exception.Message.ShouldBe(
            "The responsibility invoicing does not exist: @acme.billing.default.rst.invoicing was not found, and the lists of a responsibility that does not exist are empty. Create it, or watch a responsibility that exists.");
        exception.Key.ShouldBe("@acme.billing.default.rst.invoicing");
        exception.StatusCode.ShouldBe(404);
        exception.IsTransient.ShouldBeFalse();
        _catalog.Lists("executions").ShouldBeEmpty();
    }

    [Fact]
    public async Task TheResponsibility_IsReadUntilAPollCompleted()
    {
        _catalog.Purge(FakeCatalog.ResponsibilityKey);
        _catalog.Subscribe("northwind");
        await using var probe = Watch();

        (await probe.FirstPollAsync()).ShouldBeEmpty();
        Warnings().ShouldHaveSingleItem().Message.ShouldContain("(definitive); the 0 subscriptions held stay as they are. The responsibility invoicing does not exist");

        _catalog.Put(FakeCatalog.ResponsibilityKey);
        (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem().Kind.ShouldBe(StorytellerChangeKind.Added);
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();
        _catalog.Reads(FakeCatalog.ResponsibilityKey).Count.ShouldBe(2);
    }

    [Fact]
    public async Task AFirstPollWithExecutionsInOtherContextsOnly_WarnsOnce_NamingThem()
    {
        _catalog.Subscribe("northwind", "staging");
        _catalog.Subscribe("contoso", "dev");
        await using var probe = Watch();

        (await probe.FirstPollAsync()).ShouldBeEmpty();
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();

        Warnings().ShouldHaveSingleItem().Message.ShouldBe(
            "The first poll of @acme.billing.default.rst.invoicing found executions in the contexts dev, staging, but none in the context prod; check the name of the context.");
    }

    [Fact]
    public async Task AZeroInterval_PollsOnce()
    {
        _catalog.Subscribe("northwind");

        (await WatchOnceAsync()).ShouldHaveSingleItem();

        _fixture.Time.Created.ShouldBe(0);
        _catalog.Lists("executions").Count.ShouldBe(1);
    }

    [Fact]
    public async Task EachEnumeration_RunsItsOwnLoop()
    {
        _catalog.Subscribe("northwind");
        var watch = Catalog().WatchExecutionsAsync(FakeCatalog.Responsibility, FakeCatalog.Context, TimeSpan.Zero);

        (await ToListAsync(watch)).ShouldHaveSingleItem().Kind.ShouldBe(StorytellerChangeKind.Added);
        (await ToListAsync(watch)).ShouldHaveSingleItem().Kind.ShouldBe(StorytellerChangeKind.Added);
    }

    // Without an interval, the default of 30 s.
    [Theory]
    [InlineData(0.0, 26_999, 27_000)]
    [InlineData(0.999999, 32_998, 33_000)]
    public async Task TheNextPoll_ComesAfterTheJitteredInterval(double random, int beforeMilliseconds, int afterMilliseconds)
    {
        _catalog.Subscribe("northwind");
        await using var probe = new PollProbe<StorytellerExecutionChange>(
            Catalog(options => options.NextRandom = () => random).WatchExecutionsAsync(FakeCatalog.Responsibility, FakeCatalog.Context),
            _fixture.Time);
        await probe.FirstPollAsync();
        var timers = (_fixture.Time.Created, _fixture.Time.Active);

        // The fake clock fires a due timer within Advance, and the delay disposes its timer when it fires.
        _fixture.Time.Advance(TimeSpan.FromMilliseconds(beforeMilliseconds));
        (_fixture.Time.Created, _fixture.Time.Active).ShouldBe(timers);
        _catalog.Lists("executions").Count.ShouldBe(1);

        await probe.NextPollAsync(TimeSpan.FromMilliseconds(afterMilliseconds - beforeMilliseconds));
        _catalog.Lists("executions").Count.ShouldBe(2);
    }

    [Fact]
    public async Task ReadsRun_AtMostFourAtOnce()
    {
        foreach (var subject in Enumerable.Range(1, 10).Select(number => $"s{number}"))
        {
            _catalog.Subscribe(subject);
        }

        _catalog.Latency = TimeSpan.FromMilliseconds(20);

        (await WatchOnceAsync()).Count.ShouldBe(10);

        _catalog.MaxInFlight.ShouldBeInRange(2, 4);
    }

    [Fact]
    public async Task Cancelling_WhileWaiting_EndsWithOperationCanceledException()
    {
        _catalog.Subscribe("northwind");
        await using var probe = Watch();
        await probe.FirstPollAsync();

        probe.Stop();

        await Should.ThrowAsync<OperationCanceledException>(() => probe.Completion);
    }

    [Fact]
    public async Task Cancelling_DuringAPoll_EndsWithOperationCanceledException()
    {
        _catalog.Subscribe("northwind");
        var gate = new GatedResponse(() => StubHttpHandler.Response(HttpStatusCode.OK, TestAnnotations.Json(FakeCatalog.ResponsibilityKey)));
        _fixture.Http.Respond(gate.RespondAsync);
        using var cancellation = new CancellationTokenSource();
        await using var probe = new PollProbe<StorytellerExecutionChange>(
            Catalog().WatchExecutionsAsync(FakeCatalog.Responsibility, FakeCatalog.Context, Interval, cancellation.Token),
            _fixture.Time);

        await gate.Entered;
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => probe.Completion);
        probe.Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", "prod", "responsibility")]
    [InlineData(" ", "prod", "responsibility")]
    [InlineData("invoicing", "", "context")]
    [InlineData("in.voicing", "prod", "responsibility")]
    [InlineData("invoicing", "pr.od", "context")]
    public void ANameThatIsNotOneSegment_IsRefusedAtTheCall(string responsibility, string context, string parameter)
    {
        var catalog = Catalog();

        Should.Throw<ArgumentException>(() => catalog.WatchExecutionsAsync(responsibility, context)).ParamName.ShouldBe(parameter);
        _fixture.Http.Requests.ShouldBeEmpty();
    }

    // The rule of the subscriptions' PollInterval: zero, or 1 s to 45 days, so milliseconds passed for seconds do not
    // poll in a tight loop.
    [Theory]
    [InlineData(-1_000)]
    [InlineData(1)]
    [InlineData(999)]
    [InlineData(46 * 24 * 3600 * 1000L)]
    public void AnIntervalOutsideTheRule_IsRefusedAtTheCall(long milliseconds)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => Catalog().WatchExecutionsAsync(FakeCatalog.Responsibility, FakeCatalog.Context, TimeSpan.FromMilliseconds(milliseconds)));

        exception.ParamName.ShouldBe("pollInterval");
        exception.Message.ShouldStartWith("The poll interval must be zero, for one poll, or 1 second to 45 days.");
        _fixture.Http.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_000)]
    [InlineData(45 * 24 * 3600 * 1000L)]
    public void AnIntervalWithinTheRule_IsTaken(long milliseconds)
    {
        Should.NotThrow(() => Catalog().WatchExecutionsAsync(FakeCatalog.Responsibility, FakeCatalog.Context, TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void WithoutTheOptionsKey_TheCallThrows_NamingTheVariableAndTheOption()
    {
        _fixture.Environment.Remove("STORYTELLER_KEY");

        var exception = Should.Throw<StorytellerConfigurationException>(() => Catalog().WatchExecutionsAsync(FakeCatalog.Responsibility, FakeCatalog.Context));

        exception.Message.ShouldContain("STORYTELLER_KEY");
        exception.Message.ShouldContain("StorytellerConfigurationOptions.Key");
        _fixture.Http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task AKeyOfAnotherTypeInTheView_ScopesTheInformer()
    {
        _fixture.Environment["STORYTELLER_KEY"] = Full(FakeCatalog.ResponsibilityKey);
        _catalog.Subscribe("northwind");

        (await WatchOnceAsync()).ShouldHaveSingleItem().Execution.Key.ShouldBe(Full(Execution("northwind")));
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }

    private static string Execution(string subject, string context = FakeCatalog.Context)
    {
        return FakeCatalog.ExecutionKey(subject, context);
    }

    private static string Usage(string subject)
    {
        return FakeCatalog.UsageKey(subject);
    }

    private static string Full(string annotationKey)
    {
        return FakeCatalog.Full(annotationKey);
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

    private IReadOnlyList<RecordingLoggerFactory.LogEntry> Warnings()
    {
        return _fixture.Logs.Entries.Where(entry => entry.Category == LogCategory && entry.Level >= LogLevel.Warning).ToList();
    }

    private ISubscriptionCatalog Catalog(Action<StorytellerConfigurationOptions>? configure = null)
    {
        return _fixture.BuildServices(configure).GetRequiredService<ISubscriptionCatalog>();
    }

    private PollProbe<StorytellerExecutionChange> Watch()
    {
        return new PollProbe<StorytellerExecutionChange>(
            Catalog().WatchExecutionsAsync(FakeCatalog.Responsibility, FakeCatalog.Context, Interval),
            _fixture.Time);
    }

    private Task<IReadOnlyList<StorytellerExecutionChange>> WatchOnceAsync()
    {
        return ToListAsync(Catalog().WatchExecutionsAsync(FakeCatalog.Responsibility, FakeCatalog.Context, TimeSpan.Zero));
    }
}
