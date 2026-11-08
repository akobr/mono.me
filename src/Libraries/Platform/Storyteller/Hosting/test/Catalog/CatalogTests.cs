using System.Net;
using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Informer;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Catalog;

// IStorytellerCatalog's GetAsync and ListAsync over the descendants routes and point reads of the fake API.
public sealed class CatalogTests : IDisposable
{
    private const string Responsibility = FakeCatalog.ResponsibilityKey;
    private const string Daily = "unt.invoicing.daily";
    private const string Monthly = "unt.invoicing.monthly";

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

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => Catalog().ListAsync(StorytellerTarget.UnitsOf(Responsibility)));

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

    [Fact]
    public async Task ListAsync_OfAMachineRestrictedBelowTheAncestor_SaysWhatItNeeds()
    {
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

    public void Dispose()
    {
        _fixture.Dispose();
    }

    private IStorytellerCatalog Catalog()
    {
        return _fixture.BuildServices().GetRequiredService<IStorytellerCatalog>();
    }
}
