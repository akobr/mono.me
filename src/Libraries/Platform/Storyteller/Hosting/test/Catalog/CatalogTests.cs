using System.Net;
using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Informer;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Catalog;

// IStorytellerCatalog's GetAsync, GetWithAncestorsAsync, ListAsync and ListWithAncestorsAsync over the descendants
// routes and point reads of the fake API.
public sealed class CatalogTests : IDisposable
{
    private const string Responsibility = FakeCatalog.ResponsibilityKey;
    private const string Daily = "unt.invoicing.daily";
    private const string Monthly = "unt.invoicing.monthly";
    private const string Northwind = "sbt.northwind";
    private const string Contoso = "sbt.contoso";
    private const string NorthwindUsage = "usg.northwind.invoicing";
    private const string ContosoUsage = "usg.contoso.invoicing";
    private const string NorthwindInProd = "cnt.northwind.prod";
    private const string NorthwindInDev = "cnt.northwind.dev";
    private const string ContosoInProd = "cnt.contoso.prod";
    private const string NorthwindProd = "exe.northwind.invoicing.prod";
    private const string NorthwindDev = "exe.northwind.invoicing.dev";
    private const string ContosoProd = "exe.contoso.invoicing.prod";

    // The distinct ancestors of the three executions, but the responsibility: two subjects, two usages and three
    // contexts.
    private static readonly string[] OtherAncestors = [Northwind, Contoso, NorthwindUsage, ContosoUsage, NorthwindInProd, NorthwindInDev, ContosoInProd];

    private readonly HostingFixture _fixture = new();
    private readonly FakeCatalog _catalog;

    public CatalogTests()
    {
        _catalog = new FakeCatalog(_fixture);
    }

    private DateTimeOffset Now => _fixture.Time.GetUtcNow();

    [Fact]
    public async Task GetAsync_ReadsAnAnnotationOfAnyType_InAnyStatus_AsTheSnapshotOfItsType()
    {
        _catalog.Put(Daily, json =>
        {
            json["UnitType"] = "job";
            json["IsDisabled"] = true;
        });

        var unit = (await Catalog().GetAsync(Daily)).ShouldBeOfType<StorytellerUnit>();

        unit.Key.ShouldBe(FakeCatalog.Full(Daily));
        unit.UnitType.ShouldBe("job");
        unit.GetStatus(Now).ShouldBe(AnnotationStatus.Disabled);
        var request = _fixture.AnnotationRequests.ShouldHaveSingleItem();
        request.Uri.AbsolutePath.ShouldBe(HostingFixture.AnnotationPath(Daily));
        request.Uri.Query.ShouldBe("?includeInactive=true&includeDeleted=true");
    }

    [Fact]
    public async Task GetAsyncOfAType_ReturnsItTyped()
    {
        _catalog.Relate(Responsibility, "UnitNames", "daily", "monthly");

        var responsibility = (await Catalog().GetAsync<StorytellerResponsibility>(Responsibility)).ShouldNotBeNull();

        responsibility.UnitNames.ShouldBe(["daily", "monthly"]);
        (await Catalog().GetAsync<StorytellerAnnotation>(Responsibility)).ShouldBeOfType<StorytellerResponsibility>();
    }

    [Fact]
    public async Task GetAsyncOfAType_RefusesAKeyOfAnotherType_BeforeAnyRead()
    {
        var exception = await Should.ThrowAsync<ArgumentException>(() => Catalog().GetAsync<StorytellerUnit>(Responsibility));

        exception.ParamName.ShouldBe("key");
        exception.Message.ShouldStartWith($"'{FakeCatalog.Full(Responsibility)}' does not name a unit, unt.{{responsibility}}.{{unit}}.");
        _fixture.Http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAsyncOfATypeOfTheApplication_ThrowsBeforeAnyRead()
    {
        var exception = await Should.ThrowAsync<ArgumentException>(() => Catalog().GetAsync<ApplicationAnnotation>(Responsibility));

        exception.ParamName.ShouldBe("TAnnotation");
        exception.Message.ShouldStartWith("ApplicationAnnotation names no annotation type");
        _fixture.Http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAsync_OfAMissingOrADeletedAnnotation_IsNull()
    {
        _catalog.Put(Daily, TestAnnotations.Deleted());

        (await Catalog().GetAsync(Daily)).ShouldBeNull();
        (await Catalog().GetAsync<StorytellerUnit>(Monthly)).ShouldBeNull();
    }

    [Fact]
    public async Task GetAsync_ThrowsAnyOtherFailure()
    {
        _catalog.Fail(Daily, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => Catalog().GetAsync(Daily));

        exception.StatusCode.ShouldBe(403);
        exception.ErrorCode.ShouldBe("AnnotationRestricted");
    }

    [Fact]
    public async Task GetAsync_OfAnAnnotationKey_WithoutTheOptionsKey_ThrowsNamingTheVariable()
    {
        _fixture.Environment.Remove("STORYTELLER_KEY");

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => Catalog().GetAsync(Daily));

        exception.Message.ShouldContain("STORYTELLER_KEY");
        _fixture.Http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListAsync_TheUnitsOfAResponsibility_ArePointReadSnapshots_ByKey()
    {
        _catalog.Put(Monthly, json =>
        {
            json["UnitType"] = "job";
            json["Values"] = new JObject { ["schedule"] = new JArray("0 2 1 * *") };
        });
        _catalog.Put(Daily);
        _catalog.Put("rst.payroll");
        _catalog.Put("unt.payroll.daily");

        var units = await Catalog().ListAsync(StorytellerTarget.UnitsOf(Responsibility));

        units.Select(unit => unit.Key).ShouldBe([FakeCatalog.Full(Daily), FakeCatalog.Full(Monthly)]);
        units[1].UnitType.ShouldBe("job");
        units[1].ResponsibilityName.ShouldBe("invoicing");
        units[1].UnitName.ShouldBe("monthly");
        units[1].Values["schedule"].ShouldBeOfType<JArray>().Values<string>().ShouldBe(["0 2 1 * *"]);
    }

    [Fact]
    public async Task ListAsync_ReadsTheAncestor_EveryPageOfTheList_AndEachDescendant_WithTheirFlags()
    {
        _catalog.Put(Daily);
        _catalog.Put(Monthly);
        _catalog.PageSize = 1;

        await Catalog().ListAsync(StorytellerTarget.UnitsOf(Responsibility));

        var requests = _fixture.AnnotationRequests;
        requests[0].Uri.AbsolutePath.ShouldBe(HostingFixture.AnnotationPath(Responsibility));
        requests[0].Uri.Query.ShouldBe("?includeInactive=true");
        var lists = _catalog.Lists(Responsibility, "units");
        lists.Count.ShouldBe(2);
        lists[0].Uri.Query.ShouldBe(string.Empty);
        lists[1].Uri.Query.ShouldStartWith("?continuationToken=");
        _catalog.Reads(Daily).ShouldHaveSingleItem().Uri.Query.ShouldBe("?includeInactive=true");
        _catalog.Reads(Monthly).ShouldHaveSingleItem().Uri.Query.ShouldBe("?includeInactive=true");
        requests.Count.ShouldBe(5);
    }

    [Fact]
    public async Task ListAsync_TheExecutionsOfASubjectInAContext_ReadsOnlyThose()
    {
        _catalog.Put("sbt.northwind");
        _catalog.Put("exe.northwind.invoicing.prod");
        _catalog.Put("exe.northwind.payroll.prod");
        _catalog.Put("exe.northwind.invoicing.dev");
        _catalog.Put("exe.contoso.invoicing.prod");

        var executions = await Catalog().ListAsync(StorytellerTarget.ExecutionsOf("sbt.northwind").WithContext("prod"));

        executions.Select(execution => execution.AnnotationKey).ShouldBe(["exe.northwind.invoicing.prod", "exe.northwind.payroll.prod"]);
        executions.ShouldAllBe(execution => execution.SubjectName == "northwind" && execution.ContextName == "prod");
        _catalog.Lists("sbt.northwind", "executions").ShouldHaveSingleItem();
        _catalog.Reads("exe.northwind.invoicing.dev").ShouldBeEmpty();
        _catalog.Reads("exe.contoso.invoicing.prod").ShouldBeEmpty();
    }

    [Fact]
    public async Task ListAsync_InactiveDescendants_OnlyWhenIncluded_AndDeletedOnesNever()
    {
        _catalog.Put(Daily);
        _catalog.Put(Monthly, json => json["IsDisabled"] = true);
        _catalog.Put("unt.invoicing.yearly", json => json["ExpiresAt"] = Now.AddDays(-1).ToString("O"));
        _catalog.Put("unt.invoicing.weekly", TestAnnotations.Deleted());

        var active = await Catalog().ListAsync(StorytellerTarget.UnitsOf(Responsibility));
        var all = await Catalog().ListAsync(StorytellerTarget.UnitsOf(Responsibility).IncludingInactive());

        active.Select(unit => unit.UnitName).ShouldBe(["daily"]);
        all.Select(unit => unit.UnitName).ShouldBe(["daily", "monthly", "yearly"]);
        _catalog.Lists(Responsibility, "units").Select(request => request.Uri.Query).ShouldBe([string.Empty, "?includeInactive=true"]);
    }

    // The list is the platform's view of now; the point read decides.
    [Fact]
    public async Task ListAsync_ADescendantThatReadsInactive_OrGoneSinceTheList_IsLeftOut()
    {
        _catalog.Put(Daily);
        _catalog.Put(Monthly);
        _catalog.Put("unt.invoicing.yearly");
        _catalog.ReadAs(Monthly, json => json["IsDisabled"] = true);
        _catalog.Fail("unt.invoicing.yearly", HttpStatusCode.NotFound);

        var units = await Catalog().ListAsync(StorytellerTarget.UnitsOf(Responsibility));

        units.Select(unit => unit.UnitName).ShouldBe(["daily"]);
    }

    [Fact]
    public async Task ListAsync_AFailedReadOfADescendant_Throws_AfterEveryReadEnded()
    {
        _catalog.Put(Daily);
        _catalog.Put(Monthly);
        _catalog.Fail(Daily, HttpStatusCode.ServiceUnavailable);
        _catalog.HoldRead(Monthly);

        var list = Catalog().ListAsync(StorytellerTarget.UnitsOf(Responsibility));
        await WaitUntilAsync(() => _catalog.Reads(Daily).Count == 1 && _catalog.HeldReads == 1);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        list.IsCompleted.ShouldBeFalse();
        _catalog.ReleaseRead(Monthly);
        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => list);
        exception.Key.ShouldBe(FakeCatalog.Full(Daily));
        exception.IsTransient.ShouldBeTrue();
        _catalog.Reads(Monthly).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ListAsync_OfAnAncestorThatDoesNotExist_ThrowsADefinitive404_WithoutAList()
    {
        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => Catalog().ListAsync(StorytellerTarget.UnitsOf("rst.missing")));

        exception.Message.ShouldBe($"The annotation {FakeCatalog.Full("rst.missing")} does not exist, so it has no units to list. Create it, or target an annotation that exists.");
        exception.StatusCode.ShouldBe(404);
        exception.IsTransient.ShouldBeFalse();
        exception.Key.ShouldBe(FakeCatalog.Full("rst.missing"));
        _catalog.Lists("rst.missing", "units").ShouldBeEmpty();
    }

    // The API answers 403 AnnotationRestricted to the read of the ancestor and to its list alike; the list's words say
    // what listing needs.
    [Fact]
    public async Task ListAsync_OfAMachineRestrictedBelowTheAncestor_SaysWhatItNeeds()
    {
        _catalog.Fail(Responsibility, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson);
        _catalog.FailPage("units", null, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson, Responsibility);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => Catalog().ListAsync(StorytellerTarget.UnitsOf(Responsibility)));

        exception.Message.ShouldBe(
            $"The machine may not list the units of {FakeCatalog.Full(Responsibility)}: a machine restricted to an annotation reads that annotation and its descendants only, so listing them needs a machine restricted to {Responsibility} or to an annotation above it, or an unrestricted one (403 AnnotationRestricted): Restricted.");
        exception.IsTransient.ShouldBeFalse();
    }

    [Fact]
    public async Task ListAsync_OfAListThatDoesNotEnd_Throws()
    {
        _catalog.Put(Daily);
        _catalog.Put(Monthly);
        _catalog.PageSize = 1;
        _fixture.Http.Otherwise = (request, cancellationToken) => request.RequestUri!.AbsolutePath == FakeCatalog.ListPath(Responsibility, "units")
            ? Task.FromResult(StubHttpHandler.Response(HttpStatusCode.OK, """{ "Annotations": [], "Count": 0, "ContinuationToken": "again" }"""))
            : Task.FromResult(StubHttpHandler.Response(HttpStatusCode.OK, TestAnnotations.Json(Responsibility)));

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => Catalog().ListAsync(StorytellerTarget.UnitsOf(Responsibility)));

        exception.Message.ShouldStartWith($"Listing the units of {FakeCatalog.Full(Responsibility)} did not end: the platform answered a continuation token it had answered before.");
    }

    [Fact]
    public async Task ListAsync_OfAFullKey_ListsInItsOwnScope()
    {
        const string root = "/api/v1/acme/payroll/staging/annotations";
        _fixture.Route($"{root}/{Responsibility}", () => StubHttpHandler.Response(HttpStatusCode.OK, TestAnnotations.Json(Responsibility)));
        _fixture.Route($"{root}/{Responsibility}/units", () => StubHttpHandler.Response(HttpStatusCode.OK, $$"""{ "Annotations": [{{TestAnnotations.Json(Daily)}}], "Count": 1 }"""));
        _fixture.Route($"{root}/{Daily}", () => StubHttpHandler.Response(HttpStatusCode.OK, TestAnnotations.Json(Daily, json => json["UnitType"] = "job")));

        var units = await Catalog().ListAsync(StorytellerTarget.UnitsOf($"@acme.payroll.staging.{Responsibility}"));

        units.ShouldHaveSingleItem().Key.ShouldBe($"@acme.payroll.staging.{Daily}");
        units[0].UnitType.ShouldBe("job");
    }

    [Fact]
    public async Task ListAsync_OfAnAnnotationKey_WithoutTheOptionsKey_ThrowsNamingTheVariable()
    {
        _fixture.Environment.Remove("STORYTELLER_KEY");

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => Catalog().ListAsync(StorytellerTarget.UnitsOf(Responsibility)));

        exception.Message.ShouldContain("STORYTELLER_KEY");
        _fixture.Http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListAsync_ReadsAtMostFourAtOnce()
    {
        for (var unit = 0; unit < 12; unit++)
        {
            _catalog.Put($"unt.invoicing.u{unit:00}");
        }

        _catalog.Latency = TimeSpan.FromMilliseconds(20);

        var units = await Catalog().ListAsync(StorytellerTarget.UnitsOf(Responsibility));

        units.Count.ShouldBe(12);
        _catalog.MaxInFlight.ShouldBeLessThanOrEqualTo(4);
        _catalog.MaxInFlight.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task GetWithAncestorsAsync_ReadsTheKeyWithEverything_ThenItsAncestorsWithInactiveOnes()
    {
        _catalog.PutWithAncestors(NorthwindProd);
        _catalog.Update(Northwind, json => json["IsDisabled"] = true);

        var context = (await Catalog().GetWithAncestorsAsync(NorthwindProd)).ShouldNotBeNull();

        context.Key.ShouldBe(FakeCatalog.Full(NorthwindProd));
        context.Annotation.ShouldBeOfType<StorytellerExecution>().ContextName.ShouldBe("prod");
        context.Chain.Select(annotation => annotation.AnnotationKey).ShouldBe([NorthwindProd, NorthwindInProd, NorthwindUsage, Northwind, Responsibility]);
        context.Subject.ShouldBeOfType<StorytellerSubject>().IsDisabled.ShouldBeTrue();
        context.Unreadable.ShouldBeEmpty();
        context.ReadAt.ShouldBe(Now);
        context.GetSubscriptionStatus(Now).ShouldBe(StorytellerSubscriptionStatus.Active);
        context.GetSubscriptionStatus(Now, StorytellerStatusScope.Chain).ShouldBe(StorytellerSubscriptionStatus.Disabled);

        var requests = _fixture.AnnotationRequests;
        requests.Count.ShouldBe(5);
        requests[0].Uri.AbsolutePath.ShouldBe(HostingFixture.AnnotationPath(NorthwindProd));
        requests[0].Uri.Query.ShouldBe("?includeInactive=true&includeDeleted=true");
        requests.Skip(1).Select(request => request.Uri.AbsolutePath).ShouldBe(
            new[] { Responsibility, Northwind, NorthwindUsage, NorthwindInProd }.Select(HostingFixture.AnnotationPath),
            ignoreOrder: true);
        requests.Skip(1).ShouldAllBe(request => request.Uri.Query == "?includeInactive=true");
    }

    [Theory]
    [InlineData(Responsibility, 0)]
    [InlineData(Daily, 1)]
    [InlineData(NorthwindUsage, 2)]
    [InlineData("uxe.northwind.invoicing.prod.daily", 6)]
    public async Task GetWithAncestorsAsync_TakesAKeyOfAnyType_AndReadsOnEveryCall(string key, int ancestors)
    {
        _catalog.PutWithAncestors(key);
        var catalog = Catalog();

        var first = (await catalog.GetWithAncestorsAsync(key)).ShouldNotBeNull();
        var second = (await catalog.GetWithAncestorsAsync(key)).ShouldNotBeNull();

        first.Annotation.Type.ShouldBe(AnnotationKey.Parse(key).Type);
        first.Ancestors.Count.ShouldBe(ancestors);
        first.Chain.Count.ShouldBe(ancestors + 1);
        second.ShouldNotBeSameAs(first);
        _fixture.AnnotationRequests.Count.ShouldBe(2 * (ancestors + 1));
    }

    [Fact]
    public async Task GetWithAncestorsAsync_OfAMissingOrADeletedKey_IsNull_AndReadsNoAncestor()
    {
        _catalog.PutWithAncestors(NorthwindProd, TestAnnotations.Deleted());

        (await Catalog().GetWithAncestorsAsync(NorthwindProd)).ShouldBeNull();
        (await Catalog().GetWithAncestorsAsync(ContosoProd)).ShouldBeNull();

        _fixture.AnnotationRequests.Select(request => request.Uri.AbsolutePath).ShouldBe(
            [HostingFixture.AnnotationPath(NorthwindProd), HostingFixture.AnnotationPath(ContosoProd)]);
    }

    [Fact]
    public async Task GetWithAncestorsAsync_ARestrictedAncestor_IsUnreadable()
    {
        _catalog.PutWithAncestors(NorthwindProd);
        _catalog.Fail(Northwind, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson);
        _catalog.Fail(NorthwindInProd, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson);

        var context = (await Catalog().GetWithAncestorsAsync(NorthwindProd)).ShouldNotBeNull();

        context.Unreadable.ShouldBe([FakeCatalog.Full(Northwind), FakeCatalog.Full(NorthwindInProd)]);
        context.Subject.ShouldBeNull();
        context.Context.ShouldBeNull();
        context.Ancestors.Keys.ShouldBe([AnnotationType.Responsibility, AnnotationType.Usage], ignoreOrder: true);
        context.Chain.Select(annotation => annotation.AnnotationKey).ShouldBe([NorthwindProd, NorthwindUsage, Responsibility]);
        context.GetSubscriptionStatus(Now, StorytellerStatusScope.Chain).ShouldBe(StorytellerSubscriptionStatus.Unknown);
    }

    // A purged ancestor and a deleted one both answer 404 to a read without deleted ones.
    [Fact]
    public async Task GetWithAncestorsAsync_AMissingAncestor_IsLeftOut()
    {
        _catalog.PutWithAncestors(NorthwindProd);
        _catalog.Purge(NorthwindInProd);
        _catalog.Update(NorthwindUsage, TestAnnotations.Deleted());

        var context = (await Catalog().GetWithAncestorsAsync(NorthwindProd)).ShouldNotBeNull();

        context.Context.ShouldBeNull();
        context.Usage.ShouldBeNull();
        context.Unreadable.ShouldBeEmpty();
        context.Ancestors.Keys.ShouldBe([AnnotationType.Responsibility, AnnotationType.Subject], ignoreOrder: true);
        context.GetSubscriptionStatus(Now, StorytellerStatusScope.Chain).ShouldBe(StorytellerSubscriptionStatus.Active);
    }

    [Fact]
    public async Task GetWithAncestorsAsync_AnyOtherFailureOfAnAncestor_Throws_AfterEveryAncestorWasAsked()
    {
        _catalog.PutWithAncestors(NorthwindProd);
        _catalog.Fail(NorthwindUsage, HttpStatusCode.InternalServerError, TestAnnotations.ErrorJson);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => Catalog().GetWithAncestorsAsync(NorthwindProd));

        exception.Key.ShouldBe(FakeCatalog.Full(NorthwindUsage));
        exception.StatusCode.ShouldBe(500);
        exception.IsTransient.ShouldBeTrue();
        _fixture.AnnotationRequests.Count.ShouldBe(5);
    }

    [Fact]
    public async Task GetWithAncestorsAsync_AFailureOfTheKey_ThrowsAsGetAsyncDoes()
    {
        _catalog.PutWithAncestors(NorthwindProd);
        _catalog.Fail(NorthwindProd, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => Catalog().GetWithAncestorsAsync(NorthwindProd));

        exception.Key.ShouldBe(FakeCatalog.Full(NorthwindProd));
        exception.StatusCode.ShouldBe(403);
        exception.ErrorCode.ShouldBe("AnnotationRestricted");
        _fixture.AnnotationRequests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GetWithAncestorsAsync_OfAFullKey_ReadsItsAncestorsInItsOwnScope()
    {
        const string root = "/api/v1/acme/payroll/staging/annotations";

        foreach (var key in new[] { NorthwindUsage, Northwind, Responsibility })
        {
            _fixture.Route($"{root}/{key}", () => StubHttpHandler.Response(HttpStatusCode.OK, TestAnnotations.Json(key)));
        }

        var context = (await Catalog().GetWithAncestorsAsync("@acme.payroll.staging.USG.northwind.invoicing")).ShouldNotBeNull();

        context.Key.ShouldBe($"@acme.payroll.staging.{NorthwindUsage}");
        context.Chain.Select(annotation => annotation.Key).ShouldBe(
            [$"@acme.payroll.staging.{NorthwindUsage}", $"@acme.payroll.staging.{Northwind}", $"@acme.payroll.staging.{Responsibility}"]);
        _fixture.AnnotationRequests.ShouldAllBe(request => request.Uri.AbsolutePath.StartsWith(root, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetWithAncestorsAsync_OfAnAnnotationKey_WithoutTheOptionsKey_ThrowsNamingTheVariable()
    {
        _fixture.Environment.Remove("STORYTELLER_KEY");

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => Catalog().GetWithAncestorsAsync(NorthwindProd));

        exception.Message.ShouldContain("STORYTELLER_KEY");
        _fixture.Http.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("exe.northwind")]
    [InlineData("@acme.billing.exe.northwind.invoicing.prod")]
    public async Task GetWithAncestorsAsync_OfAKeyThatDoesNotParse_IsRefused(string key)
    {
        var exception = await Should.ThrowAsync<ArgumentException>(() => Catalog().GetWithAncestorsAsync(key));

        exception.ParamName.ShouldBe("key");
        _fixture.Http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetWithAncestorsAsync_ReadAt_IsWhenTheReadStarted()
    {
        _catalog.PutWithAncestors(NorthwindProd);
        _catalog.HoldRead(Northwind);
        var started = Now;

        var read = Catalog().GetWithAncestorsAsync(NorthwindProd);
        await WaitUntilAsync(() => _catalog.HeldReads == 1);
        _fixture.Time.Advance(TimeSpan.FromMinutes(1));
        _catalog.ReleaseRead(Northwind);

        (await read).ShouldNotBeNull().ReadAt.ShouldBe(started);
    }

    // The cost of the spec's example, smaller: the read of the responsibility and the list, a read per execution, and
    // 2 + 2 + 3 reads of the subjects, the usages and the contexts. The responsibility is not read again.
    [Fact]
    public async Task ListWithAncestorsAsync_ReadsEachDistinctAncestorOnce_TheTargetsOwnIncluded()
    {
        PutExecutions();

        var contexts = await Catalog().ListWithAncestorsAsync(StorytellerTarget.ExecutionsOf(Responsibility));

        contexts.Select(context => context.Key).ShouldBe([FakeCatalog.Full(ContosoProd), FakeCatalog.Full(NorthwindDev), FakeCatalog.Full(NorthwindProd)]);
        contexts.ShouldAllBe(context => context.Annotation is StorytellerExecution && context.Ancestors.Count == 4 && context.Unreadable.Count == 0);
        contexts[1].Chain.Select(annotation => annotation.AnnotationKey).ShouldBe([NorthwindDev, NorthwindInDev, NorthwindUsage, Northwind, Responsibility]);
        contexts[1].Subject.ShouldBeOfType<StorytellerSubject>().ShouldBeSameAs(contexts[2].Subject);
        contexts[1].Usage.ShouldBeSameAs(contexts[2].Usage);
        contexts[0].Responsibility.ShouldBeOfType<StorytellerResponsibility>().ShouldBeSameAs(contexts[2].Responsibility);

        _fixture.AnnotationRequests[0].Uri.AbsolutePath.ShouldBe(HostingFixture.AnnotationPath(Responsibility));
        _catalog.Reads(Responsibility).ShouldHaveSingleItem();
        _catalog.Lists(Responsibility, "executions").ShouldHaveSingleItem();

        foreach (var execution in new[] { ContosoProd, NorthwindDev, NorthwindProd })
        {
            _catalog.Reads(execution).ShouldHaveSingleItem();
        }

        foreach (var ancestor in OtherAncestors)
        {
            _catalog.Reads(ancestor).ShouldHaveSingleItem().Uri.Query.ShouldBe("?includeInactive=true");
        }

        _fixture.AnnotationRequests.Count.ShouldBe(2 + 3 + 7);
    }

    [Fact]
    public async Task ListWithAncestorsAsync_ListsAsListAsyncDoes_AndReadsTheAncestorsOfWhatItKeeps()
    {
        PutExecutions();
        _catalog.Update(NorthwindDev, json => json["IsDisabled"] = true);
        var target = StorytellerTarget.ExecutionsOf(Responsibility).WithSubject("northwind");

        var active = await Catalog().ListWithAncestorsAsync(target);

        active.ShouldHaveSingleItem().Annotation.AnnotationKey.ShouldBe(NorthwindProd);
        _catalog.Reads(NorthwindInDev).ShouldBeEmpty();
        _catalog.Reads(Contoso).ShouldBeEmpty();
        _catalog.Reads(ContosoProd).ShouldBeEmpty();

        var all = await Catalog().ListWithAncestorsAsync(target.IncludingInactive());

        all.Select(context => context.Annotation.AnnotationKey).ShouldBe([NorthwindDev, NorthwindProd]);
        all[0].GetSubscriptionStatus(Now, StorytellerStatusScope.Chain).ShouldBe(StorytellerSubscriptionStatus.Disabled);
        all[0].FindInactiveAnnotation(Now, StorytellerStatusScope.Chain).ShouldBeSameAs(all[0].Annotation);
        _catalog.Reads(NorthwindInDev).ShouldHaveSingleItem();
        _catalog.Lists(Responsibility, "executions").Select(request => request.Uri.Query).ShouldBe([string.Empty, "?includeInactive=true"]);
    }

    // One call gives every descendant with the status and the time zone of its chain.
    [Fact]
    public async Task ListWithAncestorsAsync_GivesTheChainStatusAndTheTimeZoneOfEachDescendant()
    {
        PutExecutions();
        _catalog.Update(Northwind, json =>
        {
            json["IsDisabled"] = true;
            json["TimeZone"] = "Europe/Prague";
        });
        _catalog.Update(NorthwindInDev, json => json["TimeZone"] = "Asia/Tokyo");

        var contexts = await Catalog().ListWithAncestorsAsync(StorytellerTarget.ExecutionsOf(Responsibility));

        contexts.Select(context => (context.Annotation.AnnotationKey, context.GetSubscriptionStatus(Now, StorytellerStatusScope.Chain), context.GetEffectiveTimeZone())).ShouldBe(
        [
            (ContosoProd, StorytellerSubscriptionStatus.Active, null),
            (NorthwindDev, StorytellerSubscriptionStatus.Disabled, "Asia/Tokyo"),
            (NorthwindProd, StorytellerSubscriptionStatus.Disabled, "Europe/Prague"),
        ]);
        contexts[2].FindInactiveAnnotation(Now, StorytellerStatusScope.Chain).ShouldBeSameAs(contexts[2].Subject);
        contexts.ShouldAllBe(context => context.GetSubscriptionStatus(Now) == StorytellerSubscriptionStatus.Active);
    }

    [Fact]
    public async Task ListWithAncestorsAsync_ARestrictedAncestor_IsUnreadableInEachContextThatHasIt()
    {
        PutExecutions();
        _catalog.Fail(Northwind, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson);

        var contexts = await Catalog().ListWithAncestorsAsync(StorytellerTarget.ExecutionsOf(Responsibility));

        contexts.Select(context => context.Annotation.AnnotationKey).ShouldBe([ContosoProd, NorthwindDev, NorthwindProd]);
        contexts[0].Unreadable.ShouldBeEmpty();
        contexts[0].GetSubscriptionStatus(Now, StorytellerStatusScope.Chain).ShouldBe(StorytellerSubscriptionStatus.Active);

        foreach (var context in contexts.Skip(1))
        {
            context.Unreadable.ShouldBe([FakeCatalog.Full(Northwind)]);
            context.Subject.ShouldBeNull();
            context.Ancestors.Count.ShouldBe(3);
            context.GetSubscriptionStatus(Now, StorytellerStatusScope.Chain).ShouldBe(StorytellerSubscriptionStatus.Unknown);
        }

        _catalog.Reads(Northwind).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ListWithAncestorsAsync_AMissingAncestor_IsLeftOut()
    {
        PutExecutions();
        _catalog.Purge(NorthwindInDev);

        var contexts = await Catalog().ListWithAncestorsAsync(StorytellerTarget.ExecutionsOf(Responsibility));

        contexts.Count.ShouldBe(3);
        contexts[1].Context.ShouldBeNull();
        contexts[1].Unreadable.ShouldBeEmpty();
        contexts[1].Ancestors.Count.ShouldBe(3);
        contexts[2].Context.ShouldNotBeNull();
    }

    [Fact]
    public async Task ListWithAncestorsAsync_AnyOtherFailureOfAnAncestor_Throws_AfterEveryAncestorWasAsked()
    {
        PutExecutions();
        _catalog.Fail(Contoso, HttpStatusCode.ServiceUnavailable);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(
            () => Catalog().ListWithAncestorsAsync(StorytellerTarget.ExecutionsOf(Responsibility)));

        exception.Key.ShouldBe(FakeCatalog.Full(Contoso));
        exception.StatusCode.ShouldBe(503);
        exception.IsTransient.ShouldBeTrue();
        _fixture.AnnotationRequests.Count.ShouldBe(2 + 3 + 7);
    }

    [Fact]
    public async Task ListWithAncestorsAsync_OfAnAncestorThatDoesNotExist_ThrowsADefinitive404_WithoutAList()
    {
        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(
            () => Catalog().ListWithAncestorsAsync(StorytellerTarget.UnitsOf("rst.missing")));

        exception.Message.ShouldBe($"The annotation {FakeCatalog.Full("rst.missing")} does not exist, so it has no units to list. Create it, or target an annotation that exists.");
        exception.StatusCode.ShouldBe(404);
        exception.IsTransient.ShouldBeFalse();
        _catalog.Lists("rst.missing", "units").ShouldBeEmpty();
    }

    [Fact]
    public async Task ListWithAncestorsAsync_OfNoDescendants_ReadsNoOtherAncestor()
    {
        var contexts = await Catalog().ListWithAncestorsAsync(StorytellerTarget.ExecutionsOf(Responsibility));

        contexts.ShouldBeEmpty();
        _fixture.AnnotationRequests.Count.ShouldBe(2);
    }

    // The only ancestor of a unit is the responsibility that the list read: the same three requests as ListAsync's.
    [Fact]
    public async Task ListWithAncestorsAsync_OfAFullKey_ReadsInItsOwnScope_AndTheTargetsAncestorOnce()
    {
        const string root = "/api/v1/acme/payroll/staging/annotations";
        _fixture.Route($"{root}/{Responsibility}", () => StubHttpHandler.Response(HttpStatusCode.OK, TestAnnotations.Json(Responsibility)));
        _fixture.Route($"{root}/{Responsibility}/units", () => StubHttpHandler.Response(HttpStatusCode.OK, $$"""{ "Annotations": [{{TestAnnotations.Json(Daily)}}], "Count": 1 }"""));
        _fixture.Route($"{root}/{Daily}", () => StubHttpHandler.Response(HttpStatusCode.OK, TestAnnotations.Json(Daily)));

        var context = (await Catalog().ListWithAncestorsAsync(StorytellerTarget.UnitsOf($"@acme.payroll.staging.{Responsibility}"))).ShouldHaveSingleItem();

        context.Key.ShouldBe($"@acme.payroll.staging.{Daily}");
        context.Annotation.ShouldBeOfType<StorytellerUnit>();
        context.Responsibility.ShouldNotBeNull().Key.ShouldBe($"@acme.payroll.staging.{Responsibility}");
        _fixture.AnnotationRequests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task ListWithAncestorsAsync_ReadAt_IsWhenTheCallStarted_TheSameInEveryContext()
    {
        PutExecutions();
        _catalog.HoldRead(Contoso);
        var started = Now;

        var list = Catalog().ListWithAncestorsAsync(StorytellerTarget.ExecutionsOf(Responsibility));
        await WaitUntilAsync(() => _catalog.HeldReads == 1);
        _fixture.Time.Advance(TimeSpan.FromMinutes(1));
        _catalog.ReleaseRead(Contoso);

        (await list).Select(context => context.ReadAt).ShouldBe([started, started, started]);
    }

    // The reads of the ancestors go through the limiter of the list: twelve wait, and four are asked for at once.
    [Fact]
    public async Task ListWithAncestorsAsync_ReadsTheAncestorsAtMostFourAtOnce()
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

        var list = Catalog().ListWithAncestorsAsync(StorytellerTarget.ExecutionsOf(Responsibility));
        await WaitUntilAsync(() => _catalog.HeldReads >= 4);

        foreach (var ancestor in ancestors)
        {
            _catalog.ReleaseRead(ancestor);
        }

        (await list).Count.ShouldBe(4);
        _catalog.MaxInFlight.ShouldBe(4);
    }

    [Theory]
    [InlineData(Northwind, "IsDisabled", true, new[] { ContosoProd })]
    [InlineData(NorthwindUsage, "ExpiresAt", "2026-10-01T00:00:00Z", new[] { ContosoProd })]
    [InlineData(NorthwindInProd, "ValidFrom", "2026-11-01T00:00:00Z", new[] { ContosoProd, NorthwindDev })]
    [InlineData(Responsibility, "IsDisabled", true, new string[0])]
    public async Task ListAsync_RequiringActiveAncestors_DropsTheDescendantsOfAnInactiveAncestor(string ancestor, string member, object value, string[] kept)
    {
        PutExecutions();
        _catalog.Update(ancestor, json => json[member] = JToken.FromObject(value));
        var target = StorytellerTarget.ExecutionsOf(Responsibility);

        var required = await Catalog().ListAsync(target.RequiringActiveAncestors());
        var all = await Catalog().ListAsync(target);

        required.Select(execution => execution.AnnotationKey).ShouldBe(kept);
        all.Select(execution => execution.AnnotationKey).ShouldBe([ContosoProd, NorthwindDev, NorthwindProd]);
    }

    // A purged ancestor and a deleted one: their descendants were deleted with them, whatever the list still answers.
    [Fact]
    public async Task ListAsync_RequiringActiveAncestors_AMissingAncestor_LeavesItsDescendantsOut()
    {
        PutExecutions();
        _catalog.Purge(NorthwindInDev);
        _catalog.Update(ContosoUsage, TestAnnotations.Deleted());
        var target = StorytellerTarget.ExecutionsOf(Responsibility);

        var required = await Catalog().ListAsync(target.RequiringActiveAncestors());
        var all = await Catalog().ListAsync(target);

        required.Select(execution => execution.AnnotationKey).ShouldBe([NorthwindProd]);
        all.Count.ShouldBe(3);
    }

    // Of the two ancestors the machine may not read, the first one by key is named.
    [Fact]
    public async Task ListAsync_RequiringActiveAncestors_ARestrictedAncestor_FailsDefinitively_SayingWhatItNeeds()
    {
        PutExecutions();
        _catalog.Fail(Northwind, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson);
        _catalog.Fail(ContosoInProd, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(
            () => Catalog().ListAsync(StorytellerTarget.ExecutionsOf(Responsibility).RequiringActiveAncestors()));

        exception.Message.ShouldBe(
            $"The machine may not read {FakeCatalog.Full(ContosoInProd)}, so the catalog cannot tell which of the executions of {FakeCatalog.Full(Responsibility)} are under active ancestors. Requiring active ancestors needs a machine that may read them: an unrestricted one for the executions of a responsibility, whose subjects and contexts are not under it.");
        exception.Key.ShouldBe(FakeCatalog.Full(ContosoInProd));
        exception.StatusCode.ShouldBe(403);
        exception.ErrorCode.ShouldBe("AnnotationRestricted");
        exception.IsTransient.ShouldBeFalse();
        var read = exception.InnerException.ShouldBeOfType<StorytellerConfigurationException>();
        read.Key.ShouldBe(FakeCatalog.Full(ContosoInProd));
        read.Message.ShouldBe(
            $"The machine may not read {FakeCatalog.Full(ContosoInProd)}: a machine restricted to an annotation reads that annotation and its descendants only (403 AnnotationRestricted): Restricted.");
        _fixture.AnnotationRequests.Count.ShouldBe(2 + 3 + 7);
    }

    // The ancestors of the target are those of every descendant, so they are asked for while it has none too: the
    // answer does not depend on whether a descendant exists yet.
    [Fact]
    public async Task ListAsync_RequiringActiveAncestors_OfNoDescendants_ARestrictedAncestorOfTheTarget_FailsToo()
    {
        _catalog.PutWithAncestors(NorthwindUsage);
        _catalog.Fail(Northwind, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(
            () => Catalog().ListAsync(StorytellerTarget.ExecutionsOf(NorthwindUsage).RequiringActiveAncestors()));

        exception.Key.ShouldBe(FakeCatalog.Full(Northwind));
        exception.StatusCode.ShouldBe(403);
        exception.ErrorCode.ShouldBe("AnnotationRestricted");
        exception.IsTransient.ShouldBeFalse();
        (await Catalog().ListAsync(StorytellerTarget.ExecutionsOf(NorthwindUsage))).ShouldBeEmpty();
    }

    // Without the requirement the list asks for what it asked for before; with it, also for each distinct ancestor,
    // once.
    [Fact]
    public async Task ListAsync_ReadsTheAncestorsOfTheDescendants_OnlyWhenTheyAreRequiredActive()
    {
        PutExecutions();
        var target = StorytellerTarget.ExecutionsOf(Responsibility);

        (await Catalog().ListAsync(target)).Count.ShouldBe(3);

        _fixture.AnnotationRequests.Count.ShouldBe(2 + 3);
        _catalog.Reads(Responsibility).ShouldHaveSingleItem();
        OtherAncestors.ShouldAllBe(ancestor => _catalog.Reads(ancestor).Count == 0);

        (await Catalog().ListAsync(target.RequiringActiveAncestors())).Count.ShouldBe(3);

        _fixture.AnnotationRequests.Count.ShouldBe(2 + 3 + 2 + 3 + 7);
        _catalog.Reads(Responsibility).Count.ShouldBe(2);
        OtherAncestors.ShouldAllBe(ancestor => _catalog.Reads(ancestor).Count == 1 && _catalog.Reads(ancestor)[0].Uri.Query == "?includeInactive=true");
    }

    // IncludingInactive() decides about the descendant's own status, RequiringActiveAncestors() about its ancestors.
    [Fact]
    public async Task ListAsync_RequiringActiveAncestors_CombinesWithIncludingInactive()
    {
        PutExecutions();
        _catalog.Update(NorthwindDev, json => json["IsDisabled"] = true);
        _catalog.Update(Contoso, json => json["IsDisabled"] = true);
        var target = StorytellerTarget.ExecutionsOf(Responsibility);

        (await Keys(target)).ShouldBe([ContosoProd, NorthwindProd]);
        (await Keys(target.IncludingInactive())).ShouldBe([ContosoProd, NorthwindDev, NorthwindProd]);
        (await Keys(target.RequiringActiveAncestors())).ShouldBe([NorthwindProd]);
        (await Keys(target.IncludingInactive().RequiringActiveAncestors())).ShouldBe([NorthwindDev, NorthwindProd]);

        async Task<IEnumerable<string>> Keys(StorytellerTarget<StorytellerExecution> listed)
        {
            return (await Catalog().ListAsync(listed)).Select(execution => execution.AnnotationKey);
        }
    }

    // The ancestors are told active by the time that the descendants were: a time that passes while the ancestors are
    // read does not count.
    [Fact]
    public async Task ListAsync_RequiringActiveAncestors_TellsTheAncestorsByTheTimeOfTheDescendants()
    {
        PutExecutions();
        _catalog.Update(Northwind, json => json["ExpiresAt"] = Now.AddMinutes(1).ToString("O"));
        _catalog.HoldRead(Northwind);

        var list = Catalog().ListAsync(StorytellerTarget.ExecutionsOf(Responsibility).RequiringActiveAncestors());
        await WaitUntilAsync(() => _catalog.HeldReads == 1);
        _fixture.Time.Advance(TimeSpan.FromMinutes(2));
        _catalog.ReleaseRead(Northwind);

        (await list).Count.ShouldBe(3);
    }

    [Fact]
    public async Task ListWithAncestorsAsync_RequiringActiveAncestors_KeepsTheDescendantsUnderActiveAncestors()
    {
        PutExecutions();
        _catalog.PutWithAncestors("exe.contoso.invoicing.dev");
        _catalog.Update(Northwind, json => json["IsDisabled"] = true);
        _catalog.Purge("cnt.contoso.dev");
        var target = StorytellerTarget.ExecutionsOf(Responsibility);

        var required = await Catalog().ListWithAncestorsAsync(target.RequiringActiveAncestors());
        var all = await Catalog().ListWithAncestorsAsync(target);

        var context = required.ShouldHaveSingleItem();
        context.Annotation.AnnotationKey.ShouldBe(ContosoProd);
        context.Ancestors.Count.ShouldBe(4);
        context.GetSubscriptionStatus(Now, StorytellerStatusScope.Chain).ShouldBe(StorytellerSubscriptionStatus.Active);
        all.Count.ShouldBe(4);
        _catalog.Reads(Northwind).Count.ShouldBe(2);
    }

    [Fact]
    public async Task ListWithAncestorsAsync_RequiringActiveAncestors_ARestrictedAncestor_FailsAsListAsyncDoes()
    {
        PutExecutions();
        _catalog.Fail(Northwind, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(
            () => Catalog().ListWithAncestorsAsync(StorytellerTarget.ExecutionsOf(Responsibility).RequiringActiveAncestors()));

        exception.Message.ShouldStartWith($"The machine may not read {FakeCatalog.Full(Northwind)}, so the catalog cannot tell which of the executions of {FakeCatalog.Full(Responsibility)} are under active ancestors.");
        exception.Key.ShouldBe(FakeCatalog.Full(Northwind));
        exception.StatusCode.ShouldBe(403);
        exception.ErrorCode.ShouldBe("AnnotationRestricted");
        exception.IsTransient.ShouldBeFalse();
        exception.InnerException.ShouldBeOfType<StorytellerConfigurationException>();
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }

    private static async Task WaitUntilAsync(Func<bool> isOver)
    {
        var waiting = System.Diagnostics.Stopwatch.StartNew();

        while (!isOver())
        {
            if (waiting.Elapsed > HostingFixture.Patience)
            {
                throw new TimeoutException("The reads did not arrive.");
            }

            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    private IStorytellerCatalog Catalog()
    {
        return _fixture.BuildServices().GetRequiredService<IStorytellerCatalog>();
    }

    // Three executions of the responsibility, of two subjects in three contexts, with every ancestor of theirs, all
    // active.
    private void PutExecutions()
    {
        _catalog.PutWithAncestors(NorthwindProd);
        _catalog.PutWithAncestors(NorthwindDev);
        _catalog.PutWithAncestors(ContosoProd);
    }

    // A snapshot type an application derives for its own use.
    private sealed class ApplicationAnnotation : StorytellerAnnotation
    {
    }
}
