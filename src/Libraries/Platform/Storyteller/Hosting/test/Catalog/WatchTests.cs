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
    private const string Northwind = "sbt.northwind";
    private const string Contoso = "sbt.contoso";
    private const string NorthwindUsage = "usg.northwind.invoicing";
    private const string ContosoUsage = "usg.contoso.invoicing";
    private const string NorthwindInProd = "cnt.northwind.prod";
    private const string NorthwindInDev = "cnt.northwind.dev";
    private const string ContosoInProd = "cnt.contoso.prod";
    private const string NorthwindProd = Execution;
    private const string NorthwindDev = "exe.northwind.invoicing.dev";
    private const string ContosoProd = "exe.contoso.invoicing.prod";
    private const string DailyOfNorthwindProd = "uxe.northwind.invoicing.prod.daily";

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    // The distinct ancestors of the three executions, but the responsibility: two subjects, two usages and three
    // contexts.
    private static readonly string[] OtherAncestors = [Northwind, Contoso, NorthwindUsage, ContosoUsage, NorthwindInProd, NorthwindInDev, ContosoInProd];

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

    // An ancestor of exe.northwind.invoicing.prod, what happens to it, and the reason its descendants leave with. An
    // ancestor is read without deleted ones, so a deleted one is missing, unless the read answers it anyway.
    public static TheoryData<string, string, StorytellerRemovalReason> AncestorRemovals => new()
    {
        { Northwind, "disabled", StorytellerRemovalReason.Disabled },
        { NorthwindUsage, "scheduled", StorytellerRemovalReason.Scheduled },
        { NorthwindInProd, "expired", StorytellerRemovalReason.Expired },
        { NorthwindInProd, "purged", StorytellerRemovalReason.Missing },
        { NorthwindUsage, "deleted", StorytellerRemovalReason.Missing },
        { Northwind, "read as deleted", StorytellerRemovalReason.Deleted },
        { Responsibility, "disabled", StorytellerRemovalReason.Disabled },
    };

    // The executions of the responsibility that are under active ancestors.
    private static StorytellerTarget<StorytellerExecution> Required => StorytellerTarget.ExecutionsOf(Responsibility).RequiringActiveAncestors();

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
    public async Task APoll_GivesRemovedThenChangedThenAdded_EachByKey()
    {
        _catalog.Put("unt.invoicing.b");
        _catalog.Put("unt.invoicing.c");
        _catalog.Put("unt.invoicing.d");
        await using var probe = Watch(StorytellerTarget.UnitsOf(Responsibility));
        (await probe.FirstPollAsync()).Count.ShouldBe(3);

        _catalog.Update("unt.invoicing.b", TestAnnotations.Deleted());
        _catalog.Update("unt.invoicing.d", json => json["Title"] = "Changed");
        _catalog.Update("unt.invoicing.c", json => json["Title"] = "Changed");
        _catalog.Put("unt.invoicing.e");
        _catalog.Put("unt.invoicing.a");
        var changes = await probe.NextPollAsync(Interval);

        changes.Select(change => (change.Kind, change.Annotation.UnitName)).ShouldBe(
        [
            (StorytellerChangeKind.Removed, "b"),
            (StorytellerChangeKind.Changed, "c"),
            (StorytellerChangeKind.Changed, "d"),
            (StorytellerChangeKind.Added, "a"),
            (StorytellerChangeKind.Added, "e"),
        ]);
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

    // A failure is no answer: only a read that answers ends a held annotation.
    [Fact]
    public async Task AHeldDescendantTheListMissed_WhoseReadFails_Stays_AndWarns()
    {
        _catalog.Put(Daily);
        await using var probe = Watch(StorytellerTarget.UnitsOf(Responsibility));
        await probe.FirstPollAsync();

        _catalog.Unlist(Daily);
        _catalog.Fail(Daily, HttpStatusCode.ServiceUnavailable);
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();
        Warnings().ShouldHaveSingleItem().Message.ShouldStartWith($"Reading {FakeCatalog.Full(Daily)} failed (transient); it stays as it was, and the next poll reads it again.");

        _catalog.Heal();
        _catalog.Relist(Daily);
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();
    }

    // The API answers 403 AnnotationRestricted to the read of the ancestor and to its list alike.
    [Fact]
    public async Task AMachineRestrictedBelowTheAncestor_FailsThePoll_SayingWhatListingNeeds()
    {
        _catalog.Fail(Responsibility, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson);
        _catalog.FailPage("units", null, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson, Responsibility);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => WatchOnceAsync(StorytellerTarget.UnitsOf(Responsibility)));

        exception.Message.ShouldStartWith($"The machine may not list the units of {FakeCatalog.Full(Responsibility)}: a machine restricted to an annotation reads that annotation and its descendants only, so listing them needs a machine restricted to {Responsibility} or to an annotation above it, or an unrestricted one");
        exception.IsTransient.ShouldBeFalse();
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

    // Polling again would not fix it, and a watch that never ends would read as an empty target.
    [Fact]
    public async Task ADefinitiveFailureBeforeAnyPollCompleted_EndsTheWatch()
    {
        await using var probe = Watch(StorytellerTarget.UnitsOf("rst.missing"));

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => probe.Completion.WaitAsync(HostingFixture.Patience));

        exception.StatusCode.ShouldBe(404);
        exception.IsTransient.ShouldBeFalse();
    }

    [Fact]
    public async Task ATransientFailureBeforeAnyPollCompleted_IsPolledAgain()
    {
        _catalog.Put(Daily);
        _catalog.FailPage("units", null, HttpStatusCode.ServiceUnavailable, parent: Responsibility);
        await using var probe = Watch(StorytellerTarget.UnitsOf(Responsibility));
        (await probe.FirstPollAsync()).ShouldBeEmpty();

        _catalog.Heal();

        (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem().Kind.ShouldBe(StorytellerChangeKind.Added);
    }

    // A policy may change back: after a poll completed, a definitive failure is polled again too.
    [Fact]
    public async Task ADefinitiveFailureAfterAPollCompleted_IsPolledAgain()
    {
        _catalog.Put(Daily);
        await using var probe = Watch(StorytellerTarget.UnitsOf(Responsibility));
        await probe.FirstPollAsync();

        _catalog.FailPage("units", null, HttpStatusCode.Forbidden, TestAnnotations.ErrorJson, Responsibility);
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();
        Warnings().ShouldHaveSingleItem().Message.ShouldContain("failed (definitive); the 1 annotations held stay as they are.");

        _catalog.Heal();
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();
        probe.Completion.IsCompleted.ShouldBeFalse();
    }

    // Listing the descendants of a deleted ancestor answers what is not deleted under it, and a deletion cascades.
    [Fact]
    public async Task AnAncestorDeletedAfterTheFirstPoll_RemovesItsDescendants()
    {
        _catalog.Put(Daily);
        await using var probe = Watch(StorytellerTarget.UnitsOf(Responsibility));
        await probe.FirstPollAsync();

        _catalog.Update(Responsibility, TestAnnotations.Deleted());
        _catalog.Update(Daily, TestAnnotations.Deleted());
        var change = (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem();

        change.Kind.ShouldBe(StorytellerChangeKind.Removed);
        change.Removal.ShouldBe(new StorytellerRemoval(Daily, StorytellerRemovalReason.Deleted));
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

    // The change of an ancestor moves no fingerprint of its descendants: they stay listed, and are not read to leave.
    [Fact]
    public async Task RequiringActiveAncestors_ADisabledSubject_RemovesItsDescendants_NamingTheSubject_AndEnabledAgain_AddsThem()
    {
        PutExecutions();
        await using var probe = Watch(Required);
        (await probe.FirstPollAsync()).Count.ShouldBe(3);

        _catalog.Update(Northwind, json => json["IsDisabled"] = true);
        var removed = await probe.NextPollAsync(Interval);

        var removal = new StorytellerRemoval(Northwind, StorytellerRemovalReason.Disabled);
        removed.Select(change => (change.Kind, change.Annotation.AnnotationKey, change.Removal)).ShouldBe(
        [
            (StorytellerChangeKind.Removed, NorthwindDev, removal),
            (StorytellerChangeKind.Removed, NorthwindProd, removal),
        ]);
        _catalog.Reads(NorthwindDev).ShouldHaveSingleItem();
        _catalog.Reads(NorthwindProd).ShouldHaveSingleItem();

        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();
        _catalog.Reads(NorthwindProd).ShouldHaveSingleItem();

        _catalog.Update(Northwind, json => json["IsDisabled"] = false);
        var added = await probe.NextPollAsync(Interval);

        added.Select(change => (change.Kind, change.Annotation.AnnotationKey, change.Removal)).ShouldBe(
        [
            (StorytellerChangeKind.Added, NorthwindDev, null),
            (StorytellerChangeKind.Added, NorthwindProd, null),
        ]);
        _catalog.Reads(NorthwindProd).Count.ShouldBe(2);
    }

    // The lines that the sample of hosting.md prints of such a watch: only a Removed has a removal to tell.
    [Fact]
    public async Task RequiringActiveAncestors_TheSampleOfTheDocs_TellsTheAncestorOfARemoved_AndNoRemovalOfAnAdded()
    {
        _catalog.PutWithAncestors(NorthwindProd);
        await using var probe = Watch(Required);
        var lines = (await probe.FirstPollAsync()).Select(Line).ToList();

        _catalog.Update(Northwind, json => json["IsDisabled"] = true);
        lines.AddRange((await probe.NextPollAsync(Interval)).Select(Line));

        lines.ShouldBe(
        [
            $"Added {NorthwindProd}",
            $"Removed {NorthwindProd}: {Northwind} is Disabled",
        ]);

        static string Line(StorytellerAnnotationChange<StorytellerExecution> change)
        {
            return change.Removal is { } removal
                ? $"{change.Kind} {change.Annotation.AnnotationKey}: {removal.AnnotationKey} is {removal.Reason}"
                : $"{change.Kind} {change.Annotation.AnnotationKey}";
        }
    }

    [Theory]
    [MemberData(nameof(AncestorRemovals))]
    public async Task RequiringActiveAncestors_AHeldDescendantOfAnAncestorThatEnds_IsRemoved_WithTheAncestorAndItsReason(string ancestor, string what, StorytellerRemovalReason reason)
    {
        _catalog.PutWithAncestors(NorthwindProd, json => json["Title"] = "Held");
        await using var probe = Watch(Required);
        (await probe.FirstPollAsync()).ShouldHaveSingleItem();

        switch (what)
        {
            case "disabled":
                _catalog.Update(ancestor, json => json["IsDisabled"] = true);
                break;
            case "scheduled":
                _catalog.Update(ancestor, json => json["ValidFrom"] = Now.AddDays(1).ToString("O"));
                break;
            case "expired":
                _catalog.Update(ancestor, json => json["ExpiresAt"] = Now.AddSeconds(-1).ToString("O"));
                break;
            case "deleted":
                _catalog.Update(ancestor, TestAnnotations.Deleted());
                break;
            case "read as deleted":
                _catalog.ReadAs(ancestor, TestAnnotations.Deleted());
                break;
            default:
                _catalog.Purge(ancestor);
                break;
        }

        var change = (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem();

        change.Kind.ShouldBe(StorytellerChangeKind.Removed);
        change.Removal.ShouldBe(new StorytellerRemoval(ancestor, reason));
        change.Annotation.Key.ShouldBe(FakeCatalog.Full(NorthwindProd));
        change.Annotation.Title.ShouldBe("Held");
        _catalog.Reads(NorthwindProd).ShouldHaveSingleItem();
        _catalog.Reads(ancestor).Last().Uri.Query.ShouldBe("?includeInactive=true");
    }

    [Fact]
    public async Task RequiringActiveAncestors_TheFirstPoll_AddsOnlyTheDescendantsUnderActiveAncestors_AndReadsNoOther()
    {
        PutExecutions();
        _catalog.Update(NorthwindInDev, json => json["IsDisabled"] = true);
        _catalog.Purge(ContosoInProd);
        await using var probe = Watch(Required);

        var change = (await probe.FirstPollAsync()).ShouldHaveSingleItem();

        change.Kind.ShouldBe(StorytellerChangeKind.Added);
        change.Annotation.AnnotationKey.ShouldBe(NorthwindProd);
        _catalog.Reads(NorthwindDev).ShouldBeEmpty();
        _catalog.Reads(ContosoProd).ShouldBeEmpty();
        Warnings().ShouldBeEmpty();
        NotAdded().ShouldBe(
            [
                $"{FakeCatalog.Full(ContosoProd)} is listed, but its ancestor {FakeCatalog.Full(ContosoInProd)} is Missing; it is not added, and the next poll decides again.",
                $"{FakeCatalog.Full(NorthwindDev)} is listed, but its ancestor {FakeCatalog.Full(NorthwindInDev)} is Disabled; it is not added, and the next poll decides again.",
            ],
            ignoreOrder: true);

        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();

        _catalog.Update(NorthwindInDev, json => json["IsDisabled"] = false);
        var added = (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem();

        added.Kind.ShouldBe(StorytellerChangeKind.Added);
        added.Annotation.AnnotationKey.ShouldBe(NorthwindDev);
        _catalog.Reads(ContosoProd).ShouldBeEmpty();
    }

    // The target's own ancestor is an ancestor of every descendant, and the existence check of the first poll is its read.
    [Fact]
    public async Task RequiringActiveAncestors_TheTargetsOwnAncestorCounts()
    {
        PutExecutions();
        _catalog.Update(Responsibility, json => json["IsDisabled"] = true);
        await using var probe = Watch(Required);

        (await probe.FirstPollAsync()).ShouldBeEmpty();
        _catalog.Reads(Responsibility).ShouldHaveSingleItem();
        _catalog.Reads(NorthwindProd).ShouldBeEmpty();

        _catalog.Update(Responsibility, json => json["IsDisabled"] = false);

        (await probe.NextPollAsync(Interval)).Select(change => change.Kind).ShouldBe([StorytellerChangeKind.Added, StorytellerChangeKind.Added, StorytellerChangeKind.Added]);
        _catalog.Reads(Responsibility).Count.ShouldBe(2);
    }

    [Fact]
    public async Task RequiringActiveAncestors_AnAncestorThatExpires_RemovesItsDescendants_WithoutAWrite()
    {
        PutExecutions();
        _catalog.Update(NorthwindInProd, json => json["ExpiresAt"] = Now.AddSeconds(10).ToString("O"));
        await using var probe = Watch(Required);
        (await probe.FirstPollAsync()).Count.ShouldBe(3);

        var change = (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem();

        change.Kind.ShouldBe(StorytellerChangeKind.Removed);
        change.Annotation.AnnotationKey.ShouldBe(NorthwindProd);
        change.Removal.ShouldBe(new StorytellerRemoval(NorthwindInProd, StorytellerRemovalReason.Expired));
    }

    [Fact]
    public async Task RequiringActiveAncestors_AScheduledAncestorThatBecomesValid_AddsItsDescendants_WithoutAWrite()
    {
        PutExecutions();
        _catalog.Update(Contoso, json => json["ValidFrom"] = Now.AddSeconds(10).ToString("O"));
        await using var probe = Watch(Required);
        (await probe.FirstPollAsync()).Select(change => change.Annotation.AnnotationKey).ShouldBe([NorthwindDev, NorthwindProd]);

        var change = (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem();

        change.Kind.ShouldBe(StorytellerChangeKind.Added);
        change.Annotation.AnnotationKey.ShouldBe(ContosoProd);
    }

    // The chain of an execution: the context, the usage, the subject, the responsibility.
    [Theory]
    [InlineData(new[] { Responsibility, Northwind }, Northwind)]
    [InlineData(new[] { Northwind, NorthwindUsage }, NorthwindUsage)]
    [InlineData(new[] { NorthwindUsage, NorthwindInProd }, NorthwindInProd)]
    [InlineData(new[] { Responsibility, Northwind, NorthwindUsage, NorthwindInProd }, NorthwindInProd)]
    public async Task RequiringActiveAncestors_OfSeveralInactiveAncestors_TheNearestIsNamed(string[] disabled, string named)
    {
        _catalog.PutWithAncestors(NorthwindProd);

        var removal = await RemovalAfterAsync(Required, disabled);

        removal.ShouldBe(new StorytellerRemoval(named, StorytellerRemovalReason.Disabled));
    }

    // The chain of a unit of execution: the execution, the context, the usage, the unit, the subject, the responsibility.
    [Theory]
    [InlineData(new[] { Northwind, Daily }, Daily)]
    [InlineData(new[] { Daily, NorthwindUsage }, NorthwindUsage)]
    [InlineData(new[] { NorthwindUsage, NorthwindInProd }, NorthwindInProd)]
    [InlineData(new[] { NorthwindInProd, NorthwindProd }, NorthwindProd)]
    public async Task RequiringActiveAncestors_OfSeveralInactiveAncestors_OfAUnitOfExecution_TheNearestIsNamed(string[] disabled, string named)
    {
        _catalog.PutWithAncestors(DailyOfNorthwindProd);

        var removal = await RemovalAfterAsync(StorytellerTarget.UnitsOfExecutionOf(Responsibility).RequiringActiveAncestors(), disabled);

        removal.ShouldBe(new StorytellerRemoval(named, StorytellerRemovalReason.Disabled));
    }

    // As the execution goes before the usage in a subscription: the read of a descendant that left the list decides first.
    [Theory]
    [InlineData("disabled", StorytellerRemovalReason.Disabled)]
    [InlineData("deleted", StorytellerRemovalReason.Deleted)]
    [InlineData("purged", StorytellerRemovalReason.Missing)]
    public async Task RequiringActiveAncestors_TheDescendantsOwnEnd_IsNamedBeforeAnAncestors(string what, StorytellerRemovalReason reason)
    {
        _catalog.PutWithAncestors(NorthwindProd);
        await using var probe = Watch(Required);
        (await probe.FirstPollAsync()).ShouldHaveSingleItem();

        _catalog.Update(Northwind, json => json["IsDisabled"] = true);
        _catalog.Update(NorthwindInProd, json => json["IsDisabled"] = true);

        switch (what)
        {
            case "disabled":
                _catalog.Update(NorthwindProd, json => json["IsDisabled"] = true);
                break;
            case "deleted":
                _catalog.Update(NorthwindProd, TestAnnotations.Deleted());
                break;
            default:
                _catalog.Purge(NorthwindProd);
                break;
        }

        var change = (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem();

        change.Kind.ShouldBe(StorytellerChangeKind.Removed);
        change.Removal.ShouldBe(new StorytellerRemoval(NorthwindProd, reason));
        _catalog.Reads(NorthwindProd).Last().Uri.Query.ShouldBe("?includeInactive=true&includeDeleted=true");
    }

    // The list missed it, and its own read says that it stays: then its ancestors decide.
    [Fact]
    public async Task RequiringActiveAncestors_AHeldDescendantTheListMissed_ThatItselfStays_IsRemovedForItsAncestor()
    {
        _catalog.PutWithAncestors(NorthwindProd);
        await using var probe = Watch(Required);
        (await probe.FirstPollAsync()).ShouldHaveSingleItem();

        _catalog.Unlist(NorthwindProd);
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();
        _catalog.Reads(Northwind).Count.ShouldBe(2);

        _catalog.Update(Northwind, json => json["IsDisabled"] = true);
        var change = (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem();

        change.Kind.ShouldBe(StorytellerChangeKind.Removed);
        change.Removal.ShouldBe(new StorytellerRemoval(Northwind, StorytellerRemovalReason.Disabled));
        _catalog.Reads(NorthwindProd).Count.ShouldBe(3);
    }

    // A failure is no answer about the descendant itself, and its own end goes first: nothing is told in that poll.
    [Fact]
    public async Task RequiringActiveAncestors_AHeldDescendantTheListMissed_WhoseReadFails_Stays_AlsoUnderAnInactiveAncestor()
    {
        _catalog.PutWithAncestors(NorthwindProd);
        await using var probe = Watch(Required);
        (await probe.FirstPollAsync()).ShouldHaveSingleItem();

        _catalog.Unlist(NorthwindProd);
        _catalog.Update(Northwind, json => json["IsDisabled"] = true);
        _catalog.Fail(NorthwindProd, HttpStatusCode.ServiceUnavailable);
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();
        Warnings().ShouldHaveSingleItem().Message.ShouldStartWith($"Reading {FakeCatalog.Full(NorthwindProd)} failed (transient); it stays as it was, and the next poll reads it again.");

        _catalog.Heal();
        (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem().Removal.ShouldBe(new StorytellerRemoval(Northwind, StorytellerRemovalReason.Disabled));
    }

    // A failure is no answer: the held descendant is not removed for its ancestors, and one not held is not added, nor
    // read. The next poll decides again.
    [Fact]
    public async Task RequiringActiveAncestors_AFailedReadOfAnAncestor_KeepsTheDescendantsUnderIt_AndWarns_AndTheNextPollDecides()
    {
        _catalog.PutWithAncestors(NorthwindProd);
        _catalog.PutWithAncestors(ContosoProd);
        await using var probe = Watch(Required);
        (await probe.FirstPollAsync()).Count.ShouldBe(2);

        _catalog.PutWithAncestors(NorthwindDev);
        _catalog.Update(Contoso, json => json["IsDisabled"] = true);
        _catalog.Fail(Northwind, HttpStatusCode.ServiceUnavailable);
        _catalog.Fail(Contoso, HttpStatusCode.ServiceUnavailable);
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();

        Warnings().Select(entry => entry.Message).ShouldBe(
        [
            $"Reading {FakeCatalog.Full(Contoso)} failed (transient); the descendants under it stay as they were, and the next poll reads it again. Reading {FakeCatalog.Full(Contoso)} failed (503).",
            $"Reading {FakeCatalog.Full(Northwind)} failed (transient); the descendants under it stay as they were, and the next poll reads it again. Reading {FakeCatalog.Full(Northwind)} failed (503).",
        ]);
        _catalog.Reads(NorthwindDev).ShouldBeEmpty();
        NotAdded().ShouldHaveSingleItem().ShouldBe(
            $"{FakeCatalog.Full(NorthwindDev)} is listed, but its ancestor {FakeCatalog.Full(Northwind)} is not read; it is not added, and the next poll decides again.");

        _catalog.Heal();
        var changes = await probe.NextPollAsync(Interval);

        changes.Select(change => (change.Kind, change.Annotation.AnnotationKey, change.Removal)).ShouldBe(
        [
            (StorytellerChangeKind.Removed, ContosoProd, new StorytellerRemoval(Contoso, StorytellerRemovalReason.Disabled)),
            (StorytellerChangeKind.Added, NorthwindDev, null),
        ]);
        Warnings().Count.ShouldBe(2);
    }

    [Fact]
    public async Task RequiringActiveAncestors_UnderAFailedAncestor_AHeldDescendantStillFollowsItsOwnDiff()
    {
        PutExecutions();
        await using var probe = Watch(Required);
        (await probe.FirstPollAsync()).Count.ShouldBe(3);

        _catalog.Fail(Northwind, HttpStatusCode.ServiceUnavailable);
        _catalog.Update(NorthwindDev, json => json["Title"] = "After");
        _catalog.Update(NorthwindProd, json => json["IsDisabled"] = true);
        var changes = await probe.NextPollAsync(Interval);

        changes.Select(change => (change.Kind, change.Annotation.AnnotationKey, change.Removal)).ShouldBe(
        [
            (StorytellerChangeKind.Removed, NorthwindProd, new StorytellerRemoval(NorthwindProd, StorytellerRemovalReason.Disabled)),
            (StorytellerChangeKind.Changed, NorthwindDev, null),
        ]);
        changes[1].Annotation.Title.ShouldBe("After");
        Warnings().ShouldHaveSingleItem().Message.ShouldStartWith($"Reading {FakeCatalog.Full(Northwind)} failed (transient); the descendants under it stay as they were");
    }

    // Nothing can be told about a descendant while one of its ancestors is not known, so a nearer inactive one waits,
    // for as long as that read fails: a restriction may stay.
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, null)]
    [InlineData(HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson)]
    public async Task RequiringActiveAncestors_AFailedReadOfAnAncestor_KeepsItsDescendants_AlsoUnderAnotherInactiveAncestor(HttpStatusCode status, string? body)
    {
        _catalog.PutWithAncestors(NorthwindProd);
        await using var probe = Watch(Required);
        (await probe.FirstPollAsync()).ShouldHaveSingleItem();

        _catalog.Update(NorthwindInProd, json => json["IsDisabled"] = true);
        _catalog.Fail(Northwind, status, body);
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();
        _catalog.Reads(NorthwindProd).ShouldHaveSingleItem();

        _catalog.Heal();
        (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem().Removal.ShouldBe(new StorytellerRemoval(NorthwindInProd, StorytellerRemovalReason.Disabled));
    }

    // Of the two ancestors the machine may not read, the first one by key is named. Polling again would not fix it.
    [Fact]
    public async Task RequiringActiveAncestors_ARestrictedAncestor_BeforeAnyPollCompleted_EndsTheWatch_SayingWhatItNeeds()
    {
        PutExecutions();
        _catalog.Fail(Northwind, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson);
        _catalog.Fail(ContosoInProd, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson);
        await using var probe = Watch(Required);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => probe.Completion.WaitAsync(HostingFixture.Patience));

        exception.Message.ShouldBe(
            $"The machine may not read {FakeCatalog.Full(ContosoInProd)}, so the catalog cannot tell which of the executions of {FakeCatalog.Full(Responsibility)} are under active ancestors. Requiring active ancestors needs a machine that may read them: an unrestricted one for the executions of a responsibility, whose subjects and contexts are not under it.");
        exception.Key.ShouldBe(FakeCatalog.Full(ContosoInProd));
        exception.StatusCode.ShouldBe(403);
        exception.ErrorCode.ShouldBe("AnnotationRestricted");
        exception.IsTransient.ShouldBeFalse();
        exception.InnerException.ShouldBeOfType<StorytellerConfigurationException>().Key.ShouldBe(FakeCatalog.Full(ContosoInProd));
        probe.Items.ShouldBeEmpty();
        _catalog.Reads(NorthwindProd).ShouldBeEmpty();
        _catalog.Lists("executions").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task RequiringActiveAncestors_AnotherDefinitiveFailureOfAnAncestor_BeforeAnyPollCompleted_EndsTheWatch()
    {
        PutExecutions();
        _catalog.Fail(Northwind, HttpStatusCode.Forbidden, TestAnnotations.ErrorJson);
        await using var probe = Watch(Required);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => probe.Completion.WaitAsync(HostingFixture.Patience));

        exception.Message.ShouldStartWith($"The machine may not read {FakeCatalog.Full(Northwind)}; it needs Annotation.Read on the project");
        exception.Key.ShouldBe(FakeCatalog.Full(Northwind));
        exception.StatusCode.ShouldBe(403);
        exception.ErrorCode.ShouldBe("AccessDenied");
        exception.IsTransient.ShouldBeFalse();
        probe.Items.ShouldBeEmpty();
    }

    // The poll completes without the descendants under that ancestor, as it does without one whose own read failed.
    [Fact]
    public async Task RequiringActiveAncestors_ATransientFailureOfAnAncestor_BeforeAnyPollCompleted_IsPolledAgain()
    {
        PutExecutions();
        _catalog.Fail(Northwind, HttpStatusCode.ServiceUnavailable);
        await using var probe = Watch(Required);

        (await probe.FirstPollAsync()).ShouldHaveSingleItem().Annotation.AnnotationKey.ShouldBe(ContosoProd);
        Warnings().ShouldHaveSingleItem().Message.ShouldStartWith($"Reading {FakeCatalog.Full(Northwind)} failed (transient); the descendants under it stay as they were, and the next poll reads it again.");

        _catalog.Heal();

        (await probe.NextPollAsync(Interval)).Select(change => (change.Kind, change.Annotation.AnnotationKey)).ShouldBe(
        [
            (StorytellerChangeKind.Added, NorthwindDev),
            (StorytellerChangeKind.Added, NorthwindProd),
        ]);
    }

    // A policy may change back: after a poll completed, the watch goes on, and holds what it held.
    // The ancestors of the target are those of every descendant, so the first poll asks for them while the target has
    // no descendant too: the watch does not complete a poll that it could never add anything after.
    [Fact]
    public async Task RequiringActiveAncestors_OfNoDescendants_ARestrictedAncestorOfTheTarget_EndsTheWatch()
    {
        _catalog.PutWithAncestors(NorthwindUsage);
        _catalog.Fail(Northwind, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(
            () => WatchOnceAsync(StorytellerTarget.ExecutionsOf(NorthwindUsage).RequiringActiveAncestors()));

        exception.Key.ShouldBe(FakeCatalog.Full(Northwind));
        exception.StatusCode.ShouldBe(403);
        exception.IsTransient.ShouldBeFalse();
    }

    [Fact]
    public async Task RequiringActiveAncestors_ARestrictedAncestor_AfterAPollCompleted_IsLogged_AndKeepsWhatIsHeld()
    {
        PutExecutions();
        await using var probe = Watch(Required);
        (await probe.FirstPollAsync()).Count.ShouldBe(3);

        _catalog.Update(Northwind, json => json["IsDisabled"] = true);
        _catalog.Fail(Northwind, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson);
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();

        Warnings().ShouldHaveSingleItem().Message.ShouldBe(
            $"Reading {FakeCatalog.Full(Northwind)} failed (definitive); the descendants under it stay as they were, and the next poll reads it again. The machine may not read {FakeCatalog.Full(Northwind)}, so the catalog cannot tell which of the executions of {FakeCatalog.Full(Responsibility)} are under active ancestors. Requiring active ancestors needs a machine that may read them: an unrestricted one for the executions of a responsibility, whose subjects and contexts are not under it.");
        probe.Completion.IsCompleted.ShouldBeFalse();

        _catalog.Heal();
        (await probe.NextPollAsync(Interval)).Select(change => (change.Kind, change.Annotation.AnnotationKey)).ShouldBe(
        [
            (StorytellerChangeKind.Removed, NorthwindDev),
            (StorytellerChangeKind.Removed, NorthwindProd),
        ]);
    }

    [Fact]
    public async Task RequiringActiveAncestors_AChangeOfADescendant_IsStillChanged_UnderActiveAncestors()
    {
        PutExecutions();
        await using var probe = Watch(Required);
        (await probe.FirstPollAsync()).Count.ShouldBe(3);

        _catalog.Update(NorthwindProd, json => json["Title"] = "After");
        var change = (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem();

        change.Kind.ShouldBe(StorytellerChangeKind.Changed);
        change.Annotation.Title.ShouldBe("After");
        change.Removal.ShouldBeNull();
        _catalog.Reads(NorthwindProd).Count.ShouldBe(2);
        _catalog.Reads(NorthwindDev).ShouldHaveSingleItem();
    }

    // The removals for an ancestor and those of a descendant's own read are one group, by key. A descendant that leaves
    // for an ancestor is not Changed too, and is not read.
    [Fact]
    public async Task RequiringActiveAncestors_APoll_GivesRemovedThenChangedThenAdded_EachByKey()
    {
        foreach (var subject in new[] { "b", "c", "d", "e", "h" })
        {
            _catalog.PutWithAncestors(FakeCatalog.ExecutionKey(subject));
        }

        await using var probe = Watch(Required);
        (await probe.FirstPollAsync()).Count.ShouldBe(5);

        _catalog.Update("sbt.b", json => json["IsDisabled"] = true);
        _catalog.Update(FakeCatalog.ExecutionKey("c"), json => json["Title"] = "Changed");
        _catalog.Update(FakeCatalog.ExecutionKey("d"), TestAnnotations.Deleted());
        _catalog.Update("cnt.e.prod", json => json["IsDisabled"] = true);
        _catalog.Update(FakeCatalog.ExecutionKey("e"), json => json["Title"] = "Changed");
        _catalog.Update(FakeCatalog.ExecutionKey("h"), json => json["Title"] = "Changed");
        _catalog.PutWithAncestors(FakeCatalog.ExecutionKey("i"));
        _catalog.PutWithAncestors(FakeCatalog.ExecutionKey("a"));
        _catalog.PutWithAncestors(FakeCatalog.ExecutionKey("j"));
        _catalog.Update("usg.j.invoicing", json => json["IsDisabled"] = true);
        var changes = await probe.NextPollAsync(Interval);

        changes.Select(change => (change.Kind, change.Annotation.SubjectName, change.Removal?.AnnotationKey)).ShouldBe(
        [
            (StorytellerChangeKind.Removed, "b", "sbt.b"),
            (StorytellerChangeKind.Removed, "d", FakeCatalog.ExecutionKey("d")),
            (StorytellerChangeKind.Removed, "e", "cnt.e.prod"),
            (StorytellerChangeKind.Changed, "c", null),
            (StorytellerChangeKind.Changed, "h", null),
            (StorytellerChangeKind.Added, "a", null),
            (StorytellerChangeKind.Added, "i", null),
        ]);
        _catalog.Reads(FakeCatalog.ExecutionKey("e")).ShouldHaveSingleItem();
        _catalog.Reads(FakeCatalog.ExecutionKey("j")).ShouldBeEmpty();
    }

    // The existence check of the first poll serves as the read of the target's ancestor, so every poll costs one point
    // read per distinct ancestor. They are read after the list and before the descendants.
    [Fact]
    public async Task RequiringActiveAncestors_APoll_ReadsEachDistinctAncestorOnce_AfterTheList_AndBeforeTheDescendants()
    {
        PutExecutions();
        await using var probe = Watch(Required);
        (await probe.FirstPollAsync()).Count.ShouldBe(3);

        var first = Paths();
        first.Count.ShouldBe(1 + 1 + 7 + 3);
        first[0].ShouldBe(HostingFixture.AnnotationPath(Responsibility));
        first[1].ShouldBe(FakeCatalog.ListPath("executions"));
        first.Skip(2).Take(7).ShouldBe(OtherAncestors.Select(HostingFixture.AnnotationPath), ignoreOrder: true);
        first.Skip(9).ShouldBe(new[] { NorthwindProd, NorthwindDev, ContosoProd }.Select(HostingFixture.AnnotationPath), ignoreOrder: true);
        OtherAncestors.ShouldAllBe(ancestor => _catalog.Reads(ancestor)[0].Uri.Query == "?includeInactive=true");

        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();

        var second = Paths().Skip(first.Count).ToList();
        second.Count.ShouldBe(1 + 8);
        second[0].ShouldBe(FakeCatalog.ListPath("executions"));
        second.Skip(1).ShouldBe(OtherAncestors.Append(Responsibility).Select(HostingFixture.AnnotationPath), ignoreOrder: true);

        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();

        Paths().Count.ShouldBe(first.Count + (2 * second.Count));

        IReadOnlyList<string> Paths()
        {
            return _fixture.AnnotationRequests.Select(request => request.Uri.AbsolutePath).ToList();
        }
    }

    [Fact]
    public async Task RequiringActiveAncestors_ReadsTheAncestorsOfTheDescendantsThatPassTheFilters_Only()
    {
        PutExecutions();
        await using var probe = Watch(Required.WithContext("prod"));

        (await probe.FirstPollAsync()).Select(change => change.Annotation.AnnotationKey).ShouldBe([ContosoProd, NorthwindProd]);
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();

        _catalog.Reads(NorthwindInDev).ShouldBeEmpty();
        _catalog.Reads(NorthwindDev).ShouldBeEmpty();
        _catalog.Reads(NorthwindInProd).Count.ShouldBe(2);
    }

    // The reads of the ancestors go through the limiter of the poll: twelve wait, and four are asked for at once.
    [Fact]
    public async Task RequiringActiveAncestors_ReadsTheAncestorsAtMostFourAtOnce()
    {
        var ancestors = new List<string>();

        for (var subject = 0; subject < 4; subject++)
        {
            _catalog.PutWithAncestors($"exe.s{subject}.invoicing.prod");
            ancestors.AddRange([$"sbt.s{subject}", $"usg.s{subject}.invoicing", $"cnt.s{subject}.prod"]);
        }

        foreach (var ancestor in ancestors)
        {
            _catalog.HoldRead(ancestor);
        }

        await using var probe = Watch(Required);
        await _catalog.WhenHeldAsync(4);

        foreach (var ancestor in ancestors)
        {
            _catalog.ReleaseRead(ancestor);
        }

        (await probe.FirstPollAsync()).Count.ShouldBe(4);
        _catalog.MaxInFlight.ShouldBe(4);
    }

    [Fact]
    public async Task RequiringActiveAncestors_WithAZeroInterval_TheOnePoll_AddsTheDescendantsUnderActiveAncestors()
    {
        PutExecutions();
        _catalog.Update(Northwind, json => json["IsDisabled"] = true);

        var changes = await WatchOnceAsync(Required);

        var change = changes.ShouldHaveSingleItem();
        change.Kind.ShouldBe(StorytellerChangeKind.Added);
        change.Annotation.AnnotationKey.ShouldBe(ContosoProd);
        _fixture.AnnotationRequests.Count.ShouldBe(1 + 1 + 7 + 1);
    }

    [Fact]
    public async Task RequiringActiveAncestors_WithAZeroInterval_ARestrictedAncestor_IsThrown()
    {
        PutExecutions();
        _catalog.Fail(Northwind, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => WatchOnceAsync(Required));

        exception.Message.ShouldStartWith($"The machine may not read {FakeCatalog.Full(Northwind)}, so the catalog cannot tell which of the executions of {FakeCatalog.Full(Responsibility)} are under active ancestors.");
        exception.StatusCode.ShouldBe(403);
        exception.IsTransient.ShouldBeFalse();
    }

    // There is no next poll to decide again, and an answer without the descendants under that ancestor would read as a
    // whole one: the one poll fails, as ListAsync does.
    [Fact]
    public async Task RequiringActiveAncestors_WithAZeroInterval_ATransientFailureOfAnAncestor_IsThrown()
    {
        PutExecutions();
        _catalog.Fail(Northwind, HttpStatusCode.ServiceUnavailable);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => WatchOnceAsync(Required));

        exception.Key.ShouldBe(FakeCatalog.Full(Northwind));
        exception.StatusCode.ShouldBe(503);
        exception.IsTransient.ShouldBeTrue();
    }

    // With an interval the same failure in the first poll is no end: the next poll decides again.
    [Fact]
    public async Task RequiringActiveAncestors_WithAnInterval_ATransientFailureOfAnAncestor_InTheFirstPoll_IsNotThrown()
    {
        PutExecutions();
        _catalog.Fail(Northwind, HttpStatusCode.ServiceUnavailable);
        await using var probe = Watch(Required);

        (await probe.FirstPollAsync()).ShouldHaveSingleItem().Annotation.AnnotationKey.ShouldBe(ContosoProd);
        Warnings().ShouldHaveSingleItem().Message.ShouldStartWith($"Reading {FakeCatalog.Full(Northwind)} failed (transient); the descendants under it stay as they were");
    }

    // IncludingInactive() decides about the descendant's own status, RequiringActiveAncestors() about its ancestors: an
    // inactive descendant is held, and an inactive ancestor ends it.
    [Fact]
    public async Task RequiringActiveAncestors_CombinesWithIncludingInactive()
    {
        PutExecutions();
        _catalog.Update(NorthwindDev, json => json["IsDisabled"] = true);
        _catalog.Update(Contoso, json => json["IsDisabled"] = true);
        await using var probe = Watch(Required.IncludingInactive());

        (await probe.FirstPollAsync()).Select(change => change.Annotation.AnnotationKey).ShouldBe([NorthwindDev, NorthwindProd]);

        _catalog.Update(NorthwindUsage, json => json["ExpiresAt"] = Now.AddDays(-1).ToString("O"));
        var changes = await probe.NextPollAsync(Interval);

        var removal = new StorytellerRemoval(NorthwindUsage, StorytellerRemovalReason.Expired);
        changes.Select(change => (change.Kind, change.Annotation.AnnotationKey, change.Removal)).ShouldBe(
        [
            (StorytellerChangeKind.Removed, NorthwindDev, removal),
            (StorytellerChangeKind.Removed, NorthwindProd, removal),
        ]);
    }

    [Fact]
    public async Task RequiringActiveAncestors_Cancellation_WhileTheAncestorsAreRead_EndsTheEnumeration()
    {
        _catalog.PutWithAncestors(NorthwindProd);
        _catalog.HoldRead(Northwind);
        await using var probe = Watch(Required);
        await _catalog.WhenHeldAsync(1);

        probe.Stop();

        await Should.ThrowAsync<OperationCanceledException>(() => probe.Completion.WaitAsync(HostingFixture.Patience));
    }

    // Without the requirement a poll asks for what it asked for before: no ancestor but the target's own, once.
    [Fact]
    public async Task WithoutRequiringActiveAncestors_NoAncestorOfTheDescendantsIsRead_AndAnInactiveOneRemovesNothing()
    {
        PutExecutions();
        _catalog.Update(Northwind, json => json["IsDisabled"] = true);
        await using var probe = Watch(StorytellerTarget.ExecutionsOf(Responsibility));

        (await probe.FirstPollAsync()).Count.ShouldBe(3);
        _fixture.AnnotationRequests.Count.ShouldBe(1 + 1 + 3);

        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();
        _fixture.AnnotationRequests.Count.ShouldBe(1 + 1 + 3 + 1);
        _catalog.Reads(Responsibility).ShouldHaveSingleItem();
        OtherAncestors.ShouldAllBe(ancestor => _catalog.Reads(ancestor).Count == 0);
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

    // What the informer says of the listed descendants that it does not add for an ancestor.
    private IReadOnlyList<string> NotAdded()
    {
        return _fixture.Logs.Entries
            .Where(entry => entry.Category == LogCategory && entry.Level == LogLevel.Debug && entry.Message.Contains("is not added", StringComparison.Ordinal))
            .Select(entry => entry.Message)
            .ToList();
    }

    // Three executions of the responsibility, of two subjects in three contexts, with every ancestor of theirs, all
    // active.
    private void PutExecutions()
    {
        _catalog.PutWithAncestors(NorthwindProd);
        _catalog.PutWithAncestors(NorthwindDev);
        _catalog.PutWithAncestors(ContosoProd);
    }

    // Why the one descendant of the target leaves, in the poll after its ancestors were disabled.
    private async Task<StorytellerRemoval?> RemovalAfterAsync<TAnnotation>(StorytellerTarget<TAnnotation> target, string[] disabled)
        where TAnnotation : StorytellerAnnotation
    {
        await using var probe = Watch(target);
        (await probe.FirstPollAsync()).ShouldHaveSingleItem();

        foreach (var ancestor in disabled)
        {
            _catalog.Update(ancestor, json => json["IsDisabled"] = true);
        }

        var change = (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem();
        change.Kind.ShouldBe(StorytellerChangeKind.Removed);
        return change.Removal;
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
