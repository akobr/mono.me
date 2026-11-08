using System.Net;
using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Informer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Catalog;

// IStorytellerCatalog.WatchAsync: the descendant informer over the descendants routes and point reads of the fake API.
public sealed class WatchTests : IDisposable
{
    private const string LogCategory = "_42.Platform.Storyteller.Sdk.Hosting.Informer";
    private const string Responsibility = FakeCatalog.ResponsibilityKey;
    private const string Daily = "unt.invoicing.daily";
    private const string Monthly = "unt.invoicing.monthly";
    private const string Execution = "exe.northwind.invoicing.prod";

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly HostingFixture _fixture = new();
    private readonly FakeCatalog _catalog;

    public WatchTests()
    {
        _catalog = new FakeCatalog(_fixture);
    }

    public static TheoryData<string, StorytellerRemovalReason> Removals => new()
    {
        { "deleted", StorytellerRemovalReason.Deleted },
        { "disabled", StorytellerRemovalReason.Disabled },
        { "scheduled", StorytellerRemovalReason.Scheduled },
        { "purged", StorytellerRemovalReason.Missing },
    };

    private DateTimeOffset Now => _fixture.Time.GetUtcNow();

    [Fact]
    public async Task TheFirstPoll_AddsEveryDescendant_ByKey_WithSnapshotsOfPointReads()
    {
        _catalog.Put(Monthly, json =>
        {
            json["UnitType"] = "job";
            json["Values"] = new JObject { ["days"] = new JArray(1, 15) };
        });
        _catalog.Put(Daily);

        var changes = await WatchOnceAsync(StorytellerTarget.UnitsOf(Responsibility));

        changes.Select(change => change.Kind).ShouldBe([StorytellerChangeKind.Added, StorytellerChangeKind.Added]);
        changes.Select(change => change.Annotation.Key).ShouldBe([FakeCatalog.Full(Daily), FakeCatalog.Full(Monthly)]);
        changes.ShouldAllBe(change => change.Removal == null);
        var monthly = changes[1].Annotation;
        monthly.UnitName.ShouldBe("monthly");
        monthly.UnitType.ShouldBe("job");
        monthly.Values["days"].ShouldBeOfType<JArray>().Values<int>().ShouldBe([1, 15]);
    }

    [Fact]
    public async Task TheFirstPoll_ReadsTheAncestor_TheList_AndThePointReads_WithTheirFlags()
    {
        _catalog.Put(Daily);

        await WatchOnceAsync(StorytellerTarget.UnitsOf(Responsibility));

        var requests = _fixture.AnnotationRequests;
        requests.Count.ShouldBe(3);
        requests[0].Uri.AbsolutePath.ShouldBe(HostingFixture.AnnotationPath(Responsibility));
        requests[0].Uri.Query.ShouldBe("?includeInactive=true");
        requests[1].Uri.AbsolutePath.ShouldBe(FakeCatalog.ListPath(Responsibility, "units"));
        requests[1].Uri.Query.ShouldBe(string.Empty);
        requests[2].Uri.AbsolutePath.ShouldBe(HostingFixture.AnnotationPath(Daily));
        requests[2].Uri.Query.ShouldBe("?includeInactive=true");
    }

    [Fact]
    public async Task OnlyTheDescendantsThatPassTheFilters_AreRead()
    {
        _catalog.Put("sbt.northwind");
        _catalog.Put(Execution);
        _catalog.Put("exe.northwind.invoicing.dev");
        _catalog.Put("exe.northwind.payroll.prod");

        var changes = await WatchOnceAsync(StorytellerTarget.ExecutionsOf("sbt.northwind").WithResponsibility("invoicing").WithContext("prod"));

        changes.ShouldHaveSingleItem().Annotation.Key.ShouldBe(FakeCatalog.Full(Execution));
        _catalog.Reads("exe.northwind.invoicing.dev").ShouldBeEmpty();
        _catalog.Reads("exe.northwind.payroll.prod").ShouldBeEmpty();
    }

    [Fact]
    public async Task ANewModified_IsChanged_AndOnlyThatDescendantIsReadAgain()
    {
        _catalog.Put(Daily, json => json["Title"] = "Before");
        _catalog.Put(Monthly);
        await using var probe = Watch(StorytellerTarget.UnitsOf(Responsibility));
        (await probe.FirstPollAsync()).Count.ShouldBe(2);

        _catalog.Update(Daily, json => json["Title"] = "After");
        var change = (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem();

        change.Kind.ShouldBe(StorytellerChangeKind.Changed);
        change.Annotation.Title.ShouldBe("After");
        change.Removal.ShouldBeNull();
        _catalog.Reads(Daily).Count.ShouldBe(2);
        _catalog.Reads(Monthly).Count.ShouldBe(1);
        _catalog.Reads(Responsibility).Count.ShouldBe(1);
    }

    [Fact]
    public async Task WithoutModified_AChangeIsToldByTheHashOfTheListItem()
    {
        _catalog.Put(Daily, stamp: false);
        await using var probe = Watch(StorytellerTarget.UnitsOf(Responsibility));
        await probe.FirstPollAsync();

        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();
        _catalog.Update(Daily, json => json["Labels"] = new JArray("blue"), stamp: false);

        (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem().Annotation.Labels.ShouldBe(["blue"]);
    }

    [Fact]
    public async Task ANewDescendant_IsAdded_AtTheNextPoll()
    {
        _catalog.Put(Daily);
        await using var probe = Watch(StorytellerTarget.UnitsOf(Responsibility));
        await probe.FirstPollAsync();

        _catalog.Put(Monthly);
        var change = (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem();

        change.Kind.ShouldBe(StorytellerChangeKind.Added);
        change.Annotation.Key.ShouldBe(FakeCatalog.Full(Monthly));
    }

    [Theory]
    [MemberData(nameof(Removals))]
    public async Task AHeldDescendantThatLeavesTheList_IsRemoved_WithTheReasonOfItsRead(string what, StorytellerRemovalReason reason)
    {
        _catalog.Put(Daily, json => json["Title"] = "Held");
        _catalog.Put(Monthly);
        await using var probe = Watch(StorytellerTarget.UnitsOf(Responsibility));
        await probe.FirstPollAsync();

        switch (what)
        {
            case "deleted":
                _catalog.Update(Daily, TestAnnotations.Deleted());
                break;
            case "disabled":
                _catalog.Update(Daily, json => json["IsDisabled"] = true);
                break;
            case "scheduled":
                _catalog.Update(Daily, json => json["ValidFrom"] = Now.AddDays(1).ToString("O"));
                break;
            default:
                _catalog.Purge(Daily);
                break;
        }

        var change = (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem();

        change.Kind.ShouldBe(StorytellerChangeKind.Removed);
        change.Removal.ShouldBe(new StorytellerRemoval(Daily, reason));
        change.Annotation.Title.ShouldBe("Held");
        _catalog.Reads(Daily).Last().Uri.Query.ShouldBe("?includeInactive=true&includeDeleted=true");
    }

    [Fact]
    public async Task ADescendantThatExpires_IsRemoved_WithoutAWrite()
    {
        _catalog.Put(Daily, json => json["ExpiresAt"] = Now.AddSeconds(10).ToString("O"));
        await using var probe = Watch(StorytellerTarget.UnitsOf(Responsibility));
        (await probe.FirstPollAsync()).ShouldHaveSingleItem();

        var change = (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem();

        change.Kind.ShouldBe(StorytellerChangeKind.Removed);
        change.Removal!.Reason.ShouldBe(StorytellerRemovalReason.Expired);
    }

    [Fact]
    public async Task AHeldDescendantTheListMissed_StaysWithoutAnEvent()
    {
        _catalog.Put(Daily);
        await using var probe = Watch(StorytellerTarget.UnitsOf(Responsibility));
        await probe.FirstPollAsync();

        _catalog.Unlist(Daily);
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();

        _catalog.Relist(Daily);
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();
    }

    [Fact]
    public async Task InactiveDescendants_WhenIncluded_AreHeld_AndOnlyADeletionRemovesThem()
    {
        _catalog.Put(Daily, json => json["IsDisabled"] = true);
        await using var probe = Watch(StorytellerTarget.UnitsOf(Responsibility).IncludingInactive());
        (await probe.FirstPollAsync()).ShouldHaveSingleItem().Annotation.IsDisabled.ShouldBeTrue();
        _catalog.Lists(Responsibility, "units").ShouldAllBe(request => request.Uri.Query == "?includeInactive=true");

        _catalog.Update(Daily, json => json["IsDisabled"] = false);
        (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem().Kind.ShouldBe(StorytellerChangeKind.Changed);

        _catalog.Update(Daily, json => json["ExpiresAt"] = Now.AddDays(-1).ToString("O"));
        (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem().Kind.ShouldBe(StorytellerChangeKind.Changed);

        _catalog.Update(Daily, TestAnnotations.Deleted());
        var removed = (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem();
        removed.Kind.ShouldBe(StorytellerChangeKind.Removed);
        removed.Removal!.Reason.ShouldBe(StorytellerRemovalReason.Deleted);
    }

    [Fact]
    public async Task AFailedReadOfAChange_KeepsWhatIsHeld_AndTheNextPollReadsItAgain()
    {
        _catalog.Put(Daily, json => json["Title"] = "Before");
        await using var probe = Watch(StorytellerTarget.UnitsOf(Responsibility));
        await probe.FirstPollAsync();

        _catalog.Update(Daily, json => json["Title"] = "After");
        _catalog.Fail(Daily, HttpStatusCode.ServiceUnavailable);
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();
        Warnings().ShouldHaveSingleItem().Message.ShouldStartWith($"Reading {FakeCatalog.Full(Daily)} failed (transient); it stays as it was, and the next poll reads it again.");

        _catalog.Heal();
        (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem().Annotation.Title.ShouldBe("After");
    }

    [Fact]
    public async Task ADescendantListedButReadInactive_IsNotAdded_UntilItReadsActive()
    {
        _catalog.Put(Daily);
        _catalog.ReadAs(Daily, json => json["IsDisabled"] = true);
        await using var probe = Watch(StorytellerTarget.UnitsOf(Responsibility));
        (await probe.FirstPollAsync()).ShouldBeEmpty();

        _catalog.Heal();
        (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem().Kind.ShouldBe(StorytellerChangeKind.Added);
    }

    [Fact]
    public async Task AFailedPoll_EmitsNothing_KeepsWhatIsHeld_AndWarns()
    {
        _catalog.Put(Daily);
        await using var probe = Watch(StorytellerTarget.UnitsOf(Responsibility));
        await probe.FirstPollAsync();

        _catalog.FailPage("units", null, HttpStatusCode.ServiceUnavailable, parent: Responsibility);
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();

        Warnings().ShouldHaveSingleItem().Message.ShouldBe(
            $"Polling the units of {FakeCatalog.Full(Responsibility)} failed (transient); the 1 annotations held stay as they are. Listing the units of {FakeCatalog.Full(Responsibility)} failed (503).");
        _catalog.Heal();
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();
    }

    [Fact]
    public async Task WithAZeroInterval_TheFailureOfTheOnePoll_IsThrown()
    {
        _catalog.FailPage("units", null, HttpStatusCode.ServiceUnavailable, parent: Responsibility);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => WatchOnceAsync(StorytellerTarget.UnitsOf(Responsibility)));

        exception.StatusCode.ShouldBe(503);
    }

    [Fact]
    public async Task AnAncestorThatDoesNotExist_FailsThePoll_Definitively()
    {
        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => WatchOnceAsync(StorytellerTarget.UnitsOf("rst.missing")));

        exception.Message.ShouldBe($"The annotation {FakeCatalog.Full("rst.missing")} does not exist, so it has no units to list. Create it, or target an annotation that exists.");
        exception.IsTransient.ShouldBeFalse();
        _catalog.Lists("rst.missing", "units").ShouldBeEmpty();
    }

    [Fact]
    public async Task EachEnumeration_HoldsItsOwn()
    {
        _catalog.Put(Daily);
        var watch = Catalog().WatchAsync(StorytellerTarget.UnitsOf(Responsibility), TimeSpan.Zero);

        (await ToListAsync(watch)).ShouldHaveSingleItem().Kind.ShouldBe(StorytellerChangeKind.Added);
        (await ToListAsync(watch)).ShouldHaveSingleItem().Kind.ShouldBe(StorytellerChangeKind.Added);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(500)]
    public void AnIntervalOutsideTheRule_IsRefusedAtTheCall(int milliseconds)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(() => Catalog().WatchAsync(StorytellerTarget.UnitsOf(Responsibility), TimeSpan.FromMilliseconds(milliseconds)));

        exception.ParamName.ShouldBe("pollInterval");
        _fixture.Http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public void AnAnnotationKey_WithoutTheOptionsKey_IsRefusedAtTheCall()
    {
        _fixture.Environment.Remove("STORYTELLER_KEY");

        Should.Throw<StorytellerConfigurationException>(() => Catalog().WatchAsync(StorytellerTarget.UnitsOf(Responsibility))).Message.ShouldContain("STORYTELLER_KEY");
        _fixture.Http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancellation_EndsTheEnumeration()
    {
        _catalog.Put(Daily);
        await using var probe = Watch(StorytellerTarget.UnitsOf(Responsibility));
        await probe.FirstPollAsync();

        probe.Stop();

        await Should.ThrowAsync<OperationCanceledException>(() => probe.Completion.WaitAsync(HostingFixture.Patience));
    }

    // Relation arrays do not move Modified, and list items do not carry them: the parent raises no Changed for a new
    // child, and the watch of the children raises its Added.
    [Fact]
    public async Task ANewChild_RaisesNoChangeOfItsParent_ButAnAdditionOfTheChildren()
    {
        _catalog.Put(Execution, json => json["UnitNames"] = new JArray("daily"));
        _catalog.Put($"uxe.northwind.invoicing.prod.daily");
        await using (var parents = Watch(StorytellerTarget.ExecutionsOf(Responsibility)))
        {
            (await parents.FirstPollAsync()).ShouldHaveSingleItem().Annotation.UnitNames.ShouldBe(["daily"]);

            _catalog.Put("uxe.northwind.invoicing.prod.monthly");
            _catalog.Relate(Execution, "UnitNames", "daily", "monthly");

            (await parents.NextPollAsync(Interval)).ShouldBeEmpty();
        }

        var children = await WatchOnceAsync(StorytellerTarget.UnitsOfExecutionOf(Execution));

        children.Select(change => change.Annotation.UnitName).ShouldBe(["daily", "monthly"]);
        (await Catalog().GetAsync<StorytellerExecution>(Execution)).ShouldNotBeNull().UnitNames.ShouldBe(["daily", "monthly"]);
    }

    public void Dispose()
    {
        _fixture.Dispose();
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

    private IStorytellerCatalog Catalog()
    {
        return _fixture.BuildServices().GetRequiredService<IStorytellerCatalog>();
    }

    private PollProbe<StorytellerAnnotationChange<TAnnotation>> Watch<TAnnotation>(StorytellerTarget<TAnnotation> target)
        where TAnnotation : StorytellerAnnotation
    {
        return new PollProbe<StorytellerAnnotationChange<TAnnotation>>(Catalog().WatchAsync(target, Interval), _fixture.Time);
    }

    private Task<IReadOnlyList<StorytellerAnnotationChange<TAnnotation>>> WatchOnceAsync<TAnnotation>(StorytellerTarget<TAnnotation> target)
        where TAnnotation : StorytellerAnnotation
    {
        return ToListAsync(Catalog().WatchAsync(target, TimeSpan.Zero));
    }
}
