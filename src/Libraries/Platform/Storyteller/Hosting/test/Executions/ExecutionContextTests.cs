using System.Net;
using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Hosting.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Executions;

public sealed class ExecutionContextTests : IDisposable
{
    private const string Subject = "sbt.northwind";
    private const string Responsibility = "rst.invoicing";
    private const string Usage = "usg.northwind.invoicing";
    private const string Context = "cnt.northwind.prod";
    private const string Unit = "unt.invoicing.daily";
    private const string UnitOfExecution = "uxe.northwind.invoicing.prod.daily";
    private const string LogCategory = "_42.Platform.Storyteller.Sdk.Hosting.ExecutionContext";

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    // One per place of the longest chain.
    private static readonly string[] Zones = ["Europe/Prague", "America/New_York", "Asia/Tokyo", "Australia/Sydney", "Africa/Cairo", "Europe/Oslo", "Pacific/Auckland"];

    private readonly HostingFixture _fixture = new();

    private DateTimeOffset Now => _fixture.Time.GetUtcNow();

    [Fact]
    public async Task AnExecutionKey_ReadsTheKeyWithEverything_AndItsFourAncestorsWithInactiveOnes()
    {
        _fixture.RouteExecution();

        var context = await WhoAmI().GetAsync();

        context.Key.ShouldBe(Full(HostingFixture.ExecutionKey));
        context.ReadAt.ShouldBe(_fixture.Time.GetUtcNow());
        context.Execution.ShouldBeSameAs(context.Annotation.ShouldBeOfType<StorytellerExecution>());
        context.Subject.ShouldNotBeNull().Key.ShouldBe(Full(Subject));
        context.Responsibility.ShouldNotBeNull().Key.ShouldBe(Full(Responsibility));
        context.Usage.ShouldNotBeNull().Key.ShouldBe(Full(Usage));
        context.Context.ShouldNotBeNull().Key.ShouldBe(Full(Context));
        context.Ancestors.Keys.ShouldBe([AnnotationType.Responsibility, AnnotationType.Subject, AnnotationType.Usage, AnnotationType.Context], ignoreOrder: true);
        context.Unreadable.ShouldBeEmpty();
        context.GetSubscriptionStatus(_fixture.Time.GetUtcNow()).ShouldBe(StorytellerSubscriptionStatus.Active);

        var requests = _fixture.AnnotationRequests;
        requests.Count.ShouldBe(5);
        requests[0].Uri.AbsolutePath.ShouldBe(HostingFixture.AnnotationPath(HostingFixture.ExecutionKey));
        requests[0].Uri.Query.ShouldBe("?includeInactive=true&includeDeleted=true");
        requests.Skip(1).Select(request => request.Uri.AbsolutePath).ShouldBe(
            [HostingFixture.AnnotationPath(Subject), HostingFixture.AnnotationPath(Responsibility), HostingFixture.AnnotationPath(Usage), HostingFixture.AnnotationPath(Context)],
            ignoreOrder: true);
        requests.Skip(1).ShouldAllBe(request => request.Uri.Query == "?includeInactive=true");
    }

    [Fact]
    public async Task AUnitOfExecutionKey_FillsItsExecution()
    {
        RouteUnitOfExecution();

        var context = await WhoAmI().GetAsync();

        context.Annotation.Type.ShouldBe(AnnotationType.UnitOfExecution);
        context.Annotation.ShouldNotBeOfType<StorytellerExecution>();
        var execution = context.Execution.ShouldNotBeNull();
        execution.Key.ShouldBe(Full(HostingFixture.ExecutionKey));
        execution.SubjectName.ShouldBe("northwind");
        context.Ancestors[AnnotationType.Unit].Key.ShouldBe(Full("unt.invoicing.daily"));
        context.Ancestors.Count.ShouldBe(6);
        context.Usage.ShouldNotBeNull();
        context.GetSubscriptionStatus(_fixture.Time.GetUtcNow()).ShouldBe(StorytellerSubscriptionStatus.Active);
    }

    [Fact]
    public async Task AResponsibilityKey_FillsOnlyItsResponsibility()
    {
        _fixture.Environment["STORYTELLER_KEY"] = Full(Responsibility);
        _fixture.RouteAnnotation(Responsibility, json => json["IsDisabled"] = true);

        var context = await WhoAmI().GetAsync();

        context.Responsibility.ShouldBeSameAs(context.Annotation);
        context.Subject.ShouldBeNull();
        context.Usage.ShouldBeNull();
        context.Context.ShouldBeNull();
        context.Execution.ShouldBeNull();
        context.Ancestors.ShouldBeEmpty();
        _fixture.AnnotationRequests.ShouldHaveSingleItem();
        context.GetSubscriptionStatus(_fixture.Time.GetUtcNow()).ShouldBe(StorytellerSubscriptionStatus.Disabled);
    }

    [Fact]
    public async Task RestrictedAncestors_AreUnreadable_LoggedOnce_AndNotReadAgainUntilRefreshAsync()
    {
        RouteRestrictedMachine();
        var whoAmI = WhoAmI();

        var context = await whoAmI.GetAsync();

        context.Ancestors.ShouldBeEmpty();
        context.Unreadable.ShouldBe([Full(Responsibility), Full(Subject), Full(Usage), Full(Context)], ignoreOrder: true);
        context.Execution.ShouldNotBeNull();
        context.Usage.ShouldBeNull();
        var entry = InformationLogs().ShouldHaveSingleItem();
        entry.Message.ShouldContain(Full(HostingFixture.ExecutionKey));
        entry.Message.ShouldContain(Full(Usage));
        entry.Message.ShouldContain("RefreshAsync");

        _fixture.Time.Advance(RefreshInterval);
        await whoAmI.GetAsync();
        await Background(whoAmI);
        var refreshed = await whoAmI.GetAsync();

        refreshed.ShouldNotBeSameAs(context);
        refreshed.Unreadable.ShouldBe(context.Unreadable);
        _fixture.AnnotationRequests.Count.ShouldBe(6);
        _fixture.AnnotationRequests[^1].Uri.AbsolutePath.ShouldBe(HostingFixture.AnnotationPath(HostingFixture.ExecutionKey));
        InformationLogs().Count.ShouldBe(1);

        var again = await whoAmI.RefreshAsync();

        again.Unreadable.Count.ShouldBe(4);
        _fixture.AnnotationRequests.Count.ShouldBe(11);
        InformationLogs().Count.ShouldBe(2);
    }

    [Fact]
    public async Task AnAccessDeniedAncestor_FailsTheRead()
    {
        _fixture.RouteExecution();
        _fixture.RouteStatus(Usage, HttpStatusCode.Forbidden, TestAnnotations.ErrorJson);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => WhoAmI().GetAsync());

        exception.Key.ShouldBe(Full(Usage));
        exception.StatusCode.ShouldBe(403);
        exception.ErrorCode.ShouldBe("AccessDenied");
        exception.IsTransient.ShouldBeFalse();
        exception.Message.ShouldContain("Annotation.Read");
        exception.Message.ShouldEndWith("(403 AccessDenied): No. Ask the owner.");
    }

    [Fact]
    public async Task AMissingAncestor_IsLeftOut()
    {
        _fixture.RouteExecution();
        _fixture.RouteStatus(Context, HttpStatusCode.NotFound);

        var context = await WhoAmI().GetAsync();

        context.Context.ShouldBeNull();
        context.Unreadable.ShouldBeEmpty();
        context.Ancestors.Count.ShouldBe(3);
    }

    [Fact]
    public async Task AMissingKey_ThrowsADefinitive404_AndReadsNoAncestor()
    {
        _fixture.RouteStatus(HostingFixture.ExecutionKey, HttpStatusCode.NotFound);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => WhoAmI().GetAsync());

        exception.Key.ShouldBe(Full(HostingFixture.ExecutionKey));
        exception.StatusCode.ShouldBe(404);
        exception.ErrorCode.ShouldBeNull();
        exception.IsTransient.ShouldBeFalse();
        exception.Message.ShouldBe($"The annotation {Full(HostingFixture.ExecutionKey)} does not exist (404).");
        _fixture.AnnotationRequests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WithoutAKey_BothCallsThrow_NamingTheVariableAndTheOption()
    {
        _fixture.Environment.Remove("STORYTELLER_KEY");
        var whoAmI = WhoAmI();

        var get = await Should.ThrowAsync<StorytellerConfigurationException>(() => whoAmI.GetAsync());
        var refresh = await Should.ThrowAsync<StorytellerConfigurationException>(() => whoAmI.RefreshAsync());

        get.Message.ShouldContain("STORYTELLER_KEY");
        get.Message.ShouldContain("StorytellerConfigurationOptions.Key");
        refresh.Message.ShouldBe(get.Message);
        _fixture.Http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task AFailedFirstRead_IsNotKept()
    {
        _fixture.RouteExecution();
        _fixture.Http.Respond(HttpStatusCode.ServiceUnavailable);
        var whoAmI = WhoAmI();

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => whoAmI.GetAsync());
        var context = await whoAmI.GetAsync();

        exception.IsTransient.ShouldBeTrue();
        exception.StatusCode.ShouldBe(503);
        context.Execution.ShouldNotBeNull();
    }

    [Fact]
    public async Task ConcurrentFirstCalls_ShareOneRead_AndACallersTokenEndsItsOwnWait()
    {
        _fixture.RouteExecution();
        var gate = new GatedResponse(() => StubHttpHandler.Response(HttpStatusCode.OK, TestAnnotations.Json(HostingFixture.ExecutionKey)));
        _fixture.Http.Respond(gate.RespondAsync);
        var whoAmI = WhoAmI();
        using var cancellation = new CancellationTokenSource();

        var cancelled = whoAmI.GetAsync(cancellation.Token);
        var first = whoAmI.GetAsync();
        await gate.Entered;
        var second = whoAmI.GetAsync();
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => cancelled);
        gate.Release();

        (await first).ShouldBeSameAs(await second);
        _fixture.AnnotationRequests.Count(request => request.Uri.AbsolutePath == HostingFixture.AnnotationPath(HostingFixture.ExecutionKey)).ShouldBe(1);
    }

    [Fact]
    public async Task AfterTheFirstRead_CallsDoNotWait_AndAStaleSnapshotStartsOneBackgroundRead()
    {
        _fixture.RouteExecution();
        var whoAmI = WhoAmI();
        var first = await whoAmI.GetAsync();
        (await whoAmI.GetAsync()).ShouldBeSameAs(first);
        _fixture.AnnotationRequests.Count.ShouldBe(5);

        _fixture.Time.Advance(RefreshInterval);
        var gate = new GatedResponse(() => StubHttpHandler.Response(HttpStatusCode.OK, TestAnnotations.Json(HostingFixture.ExecutionKey, json => json["Title"] = "Renamed")));
        _fixture.Http.Respond(gate.RespondAsync);

        var stale = whoAmI.GetAsync();
        await gate.Entered;
        var during = whoAmI.GetAsync();

        stale.IsCompletedSuccessfully.ShouldBeTrue();
        during.IsCompletedSuccessfully.ShouldBeTrue();
        (await stale).ShouldBeSameAs(first);
        (await during).ShouldBeSameAs(first);

        gate.Release();
        await Background(whoAmI);
        var fresh = await whoAmI.GetAsync();

        fresh.Annotation.Title.ShouldBe("Renamed");
        fresh.ReadAt.ShouldBe(first.ReadAt + RefreshInterval);
        _fixture.AnnotationRequests.Count.ShouldBe(10);
    }

    [Fact]
    public async Task ATransientBackgroundFailure_KeepsTheSnapshot_WarnsAndWaitsAnInterval()
    {
        _fixture.RouteExecution();
        var whoAmI = WhoAmI();
        var first = await whoAmI.GetAsync();
        _fixture.Time.Advance(RefreshInterval);
        _fixture.RouteStatus(HostingFixture.ExecutionKey, HttpStatusCode.ServiceUnavailable, """{ "Message": "Down for maintenance.", "ErrorCode": "Unavailable" }""");

        (await whoAmI.GetAsync()).ShouldBeSameAs(first);
        await Background(whoAmI);
        (await whoAmI.GetAsync()).ShouldBeSameAs(first);
        await Background(whoAmI);

        _fixture.AnnotationRequests.Count.ShouldBe(6);
        var warning = _fixture.Logs.Entries.Where(entry => entry.Category == LogCategory && entry.Level == LogLevel.Warning).ShouldHaveSingleItem();
        warning.Message.ShouldContain(Full(HostingFixture.ExecutionKey));
        warning.Message.ShouldContain("transient");
        warning.Exception.ShouldBeNull();

        // The next interval reads again.
        _fixture.RouteExecution();
        _fixture.Time.Advance(RefreshInterval);
        await whoAmI.GetAsync();
        await Background(whoAmI);
        (await whoAmI.GetAsync()).ShouldNotBeSameAs(first);
    }

    [Fact]
    public async Task ABackgroundReadThatFindsTheKeyGone_TurnsItDeleted()
    {
        _fixture.RouteExecution();
        var whoAmI = WhoAmI();
        await whoAmI.GetAsync();
        _fixture.Time.Advance(RefreshInterval);
        _fixture.RouteStatus(HostingFixture.ExecutionKey, HttpStatusCode.NotFound);

        await whoAmI.GetAsync();
        await Background(whoAmI);
        var gone = await whoAmI.GetAsync();

        gone.Annotation.DeletedAt.ShouldBe(_fixture.Time.GetUtcNow());
        gone.Execution.ShouldNotBeNull().DeletedAt.ShouldBe(_fixture.Time.GetUtcNow());
        gone.ReadAt.ShouldBe(_fixture.Time.GetUtcNow());
        gone.Usage.ShouldNotBeNull();
        gone.GetSubscriptionStatus(_fixture.Time.GetUtcNow()).ShouldBe(StorytellerSubscriptionStatus.Deleted);
    }

    [Fact]
    public async Task RefreshAsync_ThrowsOnFailure_AndKeepsTheSnapshot()
    {
        _fixture.RouteExecution();
        var whoAmI = WhoAmI();
        var first = await whoAmI.GetAsync();
        _fixture.RouteStatus(HostingFixture.ExecutionKey, HttpStatusCode.InternalServerError, TestAnnotations.ErrorJson);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => whoAmI.RefreshAsync());

        exception.StatusCode.ShouldBe(500);
        exception.IsTransient.ShouldBeTrue();
        (await whoAmI.GetAsync()).ShouldBeSameAs(first);
    }

    [Fact]
    public async Task RefreshAsync_ReadsNow()
    {
        _fixture.RouteExecution();
        var whoAmI = WhoAmI();
        var first = await whoAmI.GetAsync();
        _fixture.RouteAnnotation(Usage, json => json["Labels"] = new JArray("gold"));

        var refreshed = await whoAmI.RefreshAsync();

        refreshed.ShouldNotBeSameAs(first);
        refreshed.Usage.ShouldNotBeNull().Labels.ShouldBe(["gold"]);
        (await whoAmI.GetAsync()).ShouldBeSameAs(refreshed);
    }

    [Fact]
    public async Task AZeroRefreshInterval_NeverReadsInTheBackground()
    {
        _fixture.RouteExecution();
        var whoAmI = WhoAmI(TimeSpan.Zero);
        var first = await whoAmI.GetAsync();

        _fixture.Time.Advance(TimeSpan.FromDays(1));

        (await whoAmI.GetAsync()).ShouldBeSameAs(first);
        await Background(whoAmI);
        _fixture.AnnotationRequests.Count.ShouldBe(5);
    }

    [Theory]
    [InlineData(-1, StorytellerSubscriptionStatus.Scheduled)]
    [InlineData(0, StorytellerSubscriptionStatus.Active)]
    public async Task TheStatus_FollowsValidFromOfTheExecution(int seconds, StorytellerSubscriptionStatus status)
    {
        var validFrom = _fixture.Time.GetUtcNow().AddHours(1);
        _fixture.RouteExecution();
        _fixture.RouteAnnotation(HostingFixture.ExecutionKey, json => json["ValidFrom"] = validFrom.ToString("O"));

        var context = await WhoAmI().GetAsync();

        context.GetSubscriptionStatus(validFrom.AddSeconds(seconds)).ShouldBe(status);
    }

    [Theory]
    [InlineData(-1, StorytellerSubscriptionStatus.Active)]
    [InlineData(0, StorytellerSubscriptionStatus.Expired)]
    public async Task TheStatus_FollowsExpiresAtOfTheUsage(int seconds, StorytellerSubscriptionStatus status)
    {
        var expiresAt = _fixture.Time.GetUtcNow().AddDays(1);
        _fixture.RouteExecution();
        _fixture.RouteAnnotation(Usage, json => json["ExpiresAt"] = expiresAt.ToString("O"));

        var context = await WhoAmI().GetAsync();

        context.GetSubscriptionStatus(expiresAt.AddSeconds(seconds)).ShouldBe(status);
        context.Execution!.GetStatus(expiresAt).ShouldBe(AnnotationStatus.Active);
    }

    [Fact]
    public async Task ADisabledUsage_DisablesTheSubscription()
    {
        _fixture.RouteExecution();
        _fixture.RouteAnnotation(Usage, json => json["IsDisabled"] = true);

        var context = await WhoAmI().GetAsync();

        context.GetSubscriptionStatus(_fixture.Time.GetUtcNow()).ShouldBe(StorytellerSubscriptionStatus.Disabled);
    }

    [Fact]
    public async Task TheKeysOwnStatus_ComesBeforeTheUsages()
    {
        _fixture.RouteExecution();
        _fixture.RouteAnnotation(HostingFixture.ExecutionKey, json => json["IsDisabled"] = true);
        _fixture.RouteAnnotation(Usage, json => json["ExpiresAt"] = "2026-10-01T00:00:00Z");

        var context = await WhoAmI().GetAsync();

        context.GetSubscriptionStatus(_fixture.Time.GetUtcNow()).ShouldBe(StorytellerSubscriptionStatus.Disabled);
    }

    [Fact]
    public async Task AnUnreadableUsage_IsUnknown_WhileTheExecutionIsActive()
    {
        RouteRestrictedMachine();

        var context = await WhoAmI().GetAsync();

        context.GetSubscriptionStatus(_fixture.Time.GetUtcNow()).ShouldBe(StorytellerSubscriptionStatus.Unknown);
        context.Annotation.GetStatus(_fixture.Time.GetUtcNow()).ShouldBe(AnnotationStatus.Active);
    }

    [Fact]
    public async Task AnUnreadableUsage_DoesNotHideAnInactiveExecution()
    {
        RouteRestrictedMachine();
        _fixture.RouteAnnotation(HostingFixture.ExecutionKey, json => json["ExpiresAt"] = "2026-10-01T00:00:00Z");

        var context = await WhoAmI().GetAsync();

        context.GetSubscriptionStatus(_fixture.Time.GetUtcNow()).ShouldBe(StorytellerSubscriptionStatus.Expired);
    }

    [Fact]
    public async Task AUnitOfExecution_WithAnUnreadableExecution_IsUnknown_WhileTheRestIsActive()
    {
        RouteUnitOfExecution();
        _fixture.RouteStatus(HostingFixture.ExecutionKey, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson);

        var context = await WhoAmI().GetAsync();

        context.Execution.ShouldBeNull();
        context.Usage.ShouldNotBeNull();
        context.Unreadable.ShouldBe([Full(HostingFixture.ExecutionKey)]);
        context.GetSubscriptionStatus(_fixture.Time.GetUtcNow()).ShouldBe(StorytellerSubscriptionStatus.Unknown);
    }

    [Theory]
    [InlineData("IsDisabled", true, StorytellerSubscriptionStatus.Disabled)]
    [InlineData("ExpiresAt", "2026-10-01T00:00:00Z", StorytellerSubscriptionStatus.Expired)]
    [InlineData("ValidFrom", "2026-11-01T00:00:00Z", StorytellerSubscriptionStatus.Scheduled)]
    public async Task AUnitOfExecution_FollowsItsExecution(string member, object value, StorytellerSubscriptionStatus status)
    {
        RouteUnitOfExecution();
        _fixture.RouteAnnotation(HostingFixture.ExecutionKey, json => json[member] = JToken.FromObject(value));

        var context = await WhoAmI().GetAsync();

        context.Annotation.GetStatus(_fixture.Time.GetUtcNow()).ShouldBe(AnnotationStatus.Active);
        context.Usage.ShouldNotBeNull().GetStatus(_fixture.Time.GetUtcNow()).ShouldBe(AnnotationStatus.Active);
        context.GetSubscriptionStatus(_fixture.Time.GetUtcNow()).ShouldBe(status);
    }

    [Fact]
    public async Task ADeletedKey_IsAnAnswer_WithTheStatusDeleted()
    {
        _fixture.RouteExecution();
        _fixture.RouteAnnotation(HostingFixture.ExecutionKey, TestAnnotations.Deleted("2026-10-26T10:00:00Z"));

        var context = await WhoAmI().GetAsync();

        context.Annotation.DeletedAt.ShouldBe(DateTimeOffset.Parse("2026-10-26T10:00:00Z"));
        context.GetSubscriptionStatus(_fixture.Time.GetUtcNow()).ShouldBe(StorytellerSubscriptionStatus.Deleted);
    }

    [Fact]
    public async Task AResponsibilityKey_IsATypedSnapshot_WithItsUnitNames()
    {
        _fixture.Environment["STORYTELLER_KEY"] = Full(Responsibility);
        _fixture.RouteAnnotation(Responsibility, json => json["UnitNames"] = new JArray("daily", "monthly"));

        var context = await WhoAmI().GetAsync();

        var responsibility = context.Annotation.ShouldBeOfType<StorytellerResponsibility>();
        responsibility.UnitNames.ShouldBe(["daily", "monthly"]);
        context.Responsibility.ShouldBeSameAs(responsibility);
        context.Find<StorytellerResponsibility>().ShouldBeSameAs(responsibility);
        context.Find<StorytellerExecution>().ShouldBeNull();
        context.Find<StorytellerUnit>().ShouldBeNull();
    }

    [Fact]
    public async Task AUnitOfExecutionKey_HasTypedAncestors_WithTheirRelations()
    {
        RouteUnitOfExecution();
        _fixture.RouteAnnotation(Responsibility, json => json["UnitNames"] = new JArray("daily"));
        _fixture.RouteAnnotation(Subject, json =>
        {
            json["ContextNames"] = new JArray("prod");
            json["ResponsibilityNames"] = new JArray("invoicing");
        });
        _fixture.RouteAnnotation(Usage, json => json["ContextNames"] = new JArray("prod"));
        _fixture.RouteAnnotation(Context, json => json["ResponsibilityNames"] = new JArray("invoicing"));
        _fixture.RouteAnnotation("unt.invoicing.daily", json => json["UnitType"] = "job");
        _fixture.RouteAnnotation(HostingFixture.ExecutionKey, json => json["UnitNames"] = new JArray("daily"));

        var context = await WhoAmI().GetAsync();

        var unitOfExecution = context.Annotation.ShouldBeOfType<StorytellerUnitOfExecution>();
        unitOfExecution.UnitName.ShouldBe("daily");
        context.Find<StorytellerUnitOfExecution>().ShouldBeSameAs(unitOfExecution);
        context.Find<StorytellerResponsibility>().ShouldNotBeNull().UnitNames.ShouldBe(["daily"]);
        context.Find<StorytellerSubject>().ShouldNotBeNull().ContextNames.ShouldBe(["prod"]);
        context.Find<StorytellerSubject>()!.ResponsibilityNames.ShouldBe(["invoicing"]);
        context.Find<StorytellerUsage>().ShouldNotBeNull().ContextNames.ShouldBe(["prod"]);
        context.Find<StorytellerContext>().ShouldNotBeNull().ResponsibilityNames.ShouldBe(["invoicing"]);
        context.Find<StorytellerUnit>().ShouldNotBeNull().UnitType.ShouldBe("job");
        context.Find<StorytellerExecution>().ShouldBeSameAs(context.Execution);
        context.Usage.ShouldBeOfType<StorytellerUsage>();
        context.Subject.ShouldBeOfType<StorytellerSubject>();
        context.Context.ShouldBeOfType<StorytellerContext>();
        context.Ancestors[AnnotationType.Unit].ShouldBeOfType<StorytellerUnit>().UnitName.ShouldBe("daily");
    }

    [Fact]
    public async Task ABackgroundReadThatFindsAResponsibilityGone_KeepsItsTypeAndRelations()
    {
        _fixture.Environment["STORYTELLER_KEY"] = Full(Responsibility);
        _fixture.RouteAnnotation(Responsibility, json => json["UnitNames"] = new JArray("daily"));
        var whoAmI = WhoAmI();
        await whoAmI.GetAsync();
        _fixture.Time.Advance(RefreshInterval);
        _fixture.RouteStatus(Responsibility, HttpStatusCode.NotFound);

        await whoAmI.GetAsync();
        await Background(whoAmI);
        var gone = await whoAmI.GetAsync();

        var responsibility = gone.Annotation.ShouldBeOfType<StorytellerResponsibility>();
        responsibility.DeletedAt.ShouldBe(_fixture.Time.GetUtcNow());
        responsibility.UnitNames.ShouldBe(["daily"]);
        gone.GetSubscriptionStatus(_fixture.Time.GetUtcNow()).ShouldBe(StorytellerSubscriptionStatus.Deleted);
    }

    // A test double may hold base snapshots: the members give them, Find<T>() does not.
    [Fact]
    public void Find_OfABaseSnapshot_IsNull_AndOfTheBaseType_Throws()
    {
        var usage = new StorytellerAnnotation { Key = Full(Usage), AnnotationKey = Usage, Type = AnnotationType.Usage, Name = "invoicing" };
        var context = new StorytellerExecutionContext { Key = usage.Key, Annotation = usage, ReadAt = _fixture.Time.GetUtcNow() };

        context.Usage.ShouldBeSameAs(usage);
        context.Find<StorytellerUsage>().ShouldBeNull();
        Should.Throw<ArgumentException>(() => context.Find<StorytellerAnnotation>()).Message.ShouldContain("StorytellerAnnotation names no annotation type");
    }

    // The chain order: the nearer annotation first; at the same level the context, then the usage, then the unit, and
    // the subject before the responsibility.
    [Theory]
    [InlineData(UnitOfExecution, new[] { UnitOfExecution, HostingFixture.ExecutionKey, Context, Usage, Unit, Subject, Responsibility })]
    [InlineData(HostingFixture.ExecutionKey, new[] { HostingFixture.ExecutionKey, Context, Usage, Subject, Responsibility })]
    [InlineData(Usage, new[] { Usage, Subject, Responsibility })]
    [InlineData(Context, new[] { Context, Subject })]
    [InlineData(Unit, new[] { Unit, Responsibility })]
    [InlineData(Responsibility, new[] { Responsibility })]
    [InlineData(Subject, new[] { Subject })]
    public async Task TheChain_IsTheAnnotationOfTheKeyAndItsAncestors_NearestFirst(string annotationKey, string[] chain)
    {
        RouteKey(annotationKey);

        var context = await WhoAmI().GetAsync();

        context.Chain.Select(annotation => annotation.AnnotationKey).ShouldBe(chain);
        context.Chain[0].ShouldBeSameAs(context.Annotation);
        context.Chain.Skip(1).ShouldBe(context.Ancestors.Values, ignoreOrder: true);
        context.Chain.ShouldBeSameAs(context.Chain);
        context.Unreadable.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(UnitOfExecution, new[] { HostingFixture.ExecutionKey, Usage }, new[] { UnitOfExecution, Context, Unit, Subject, Responsibility })]
    [InlineData(HostingFixture.ExecutionKey, new[] { Context, Subject }, new[] { HostingFixture.ExecutionKey, Usage, Responsibility })]
    [InlineData(HostingFixture.ExecutionKey, new[] { Context, Usage, Subject, Responsibility }, new[] { HostingFixture.ExecutionKey })]
    [InlineData(Usage, new[] { Subject }, new[] { Usage, Responsibility })]
    [InlineData(Context, new[] { Subject }, new[] { Context })]
    [InlineData(Unit, new[] { Responsibility }, new[] { Unit })]
    public async Task AnUnreadableAncestor_IsNotInTheChain(string annotationKey, string[] restricted, string[] chain)
    {
        RouteKey(annotationKey);

        foreach (var ancestor in restricted)
        {
            _fixture.RouteStatus(ancestor, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson);
        }

        var context = await WhoAmI().GetAsync();

        context.Unreadable.ShouldBe(restricted.Select(Full), ignoreOrder: true);
        context.Chain.Select(annotation => annotation.AnnotationKey).ShouldBe(chain);
    }

    [Theory]
    [InlineData(UnitOfExecution, new[] { Context, Unit }, new[] { UnitOfExecution, HostingFixture.ExecutionKey, Usage, Subject, Responsibility })]
    [InlineData(HostingFixture.ExecutionKey, new[] { Usage }, new[] { HostingFixture.ExecutionKey, Context, Subject, Responsibility })]
    [InlineData(Usage, new[] { Responsibility }, new[] { Usage, Subject })]
    [InlineData(Context, new[] { Subject }, new[] { Context })]
    [InlineData(Unit, new[] { Responsibility }, new[] { Unit })]
    public async Task AMissingAncestor_IsNotInTheChain(string annotationKey, string[] missing, string[] chain)
    {
        RouteKey(annotationKey);

        foreach (var ancestor in missing)
        {
            _fixture.RouteStatus(ancestor, HttpStatusCode.NotFound);
        }

        var context = await WhoAmI().GetAsync();

        context.Unreadable.ShouldBeEmpty();
        context.Chain.Select(annotation => annotation.AnnotationKey).ShouldBe(chain);
    }

    // A test double of who am I has the chain too: it comes from Annotation and Ancestors, not from a read.
    [Fact]
    public void AContextThatAnApplicationBuilt_HasItsChain()
    {
        var execution = Snapshot(HostingFixture.ExecutionKey);
        var usage = Snapshot(Usage);
        var subject = Snapshot(Subject);
        var responsibility = Snapshot(Responsibility);
        var ancestors = new Dictionary<AnnotationType, StorytellerAnnotation>
        {
            [AnnotationType.Responsibility] = responsibility,
            [AnnotationType.Subject] = subject,
            [AnnotationType.Usage] = usage,
        };
        var context = new StorytellerExecutionContext
        {
            Key = execution.Key,
            Annotation = execution,
            Ancestors = ancestors,
            Unreadable = [Full(Context)],
            ReadAt = _fixture.Time.GetUtcNow(),
        };

        ancestors.Clear();

        context.Chain.ShouldBe([execution, usage, subject, responsibility]);
        context.Chain.ShouldBeSameAs(context.Chain);
        Should.Throw<NotSupportedException>(() => ((IList<StorytellerAnnotation>)context.Chain).Add(usage));
    }

    [Fact]
    public void AContextBuiltWithoutAncestors_HasTheChainOfItsAnnotationAlone()
    {
        var usage = Snapshot(Usage);
        var context = new StorytellerExecutionContext { Key = usage.Key, Annotation = usage, ReadAt = _fixture.Time.GetUtcNow() };

        context.Chain.ShouldHaveSingleItem().ShouldBeSameAs(usage);
        context.GetEffectiveTimeZone().ShouldBeNull();
    }

    // Only a test double can put an ancestor of the key's own type there. The members give the key's annotation for
    // that type, so the chain leaves the ancestor out, and its zone with it.
    [Fact]
    public void AnAncestorOfTheTypeOfTheKey_IsNotInTheChain()
    {
        var usage = Snapshot(Usage);
        var context = new StorytellerExecutionContext
        {
            Key = usage.Key,
            Annotation = usage,
            Ancestors = new Dictionary<AnnotationType, StorytellerAnnotation> { [AnnotationType.Usage] = Snapshot("usg.other.invoicing", "Europe/Oslo") },
            ReadAt = _fixture.Time.GetUtcNow(),
        };

        context.Usage.ShouldBeSameAs(usage);
        context.Chain.ShouldHaveSingleItem().ShouldBeSameAs(usage);
        context.GetEffectiveTimeZone().ShouldBeNull();
    }

    // The deleted copy of who am I builds its own chain, also when the chain of the previous snapshot was read before.
    [Fact]
    public async Task ABackgroundReadThatFindsTheKeyGone_HeadsTheChainWithTheDeletedAnnotation_AndKeepsItsTimeZone()
    {
        _fixture.RouteExecution();
        _fixture.RouteAnnotation(HostingFixture.ExecutionKey, json => json["TimeZone"] = "Europe/Prague");
        var whoAmI = WhoAmI();
        var first = await whoAmI.GetAsync();
        var chain = first.Chain;
        _fixture.Time.Advance(RefreshInterval);
        _fixture.RouteStatus(HostingFixture.ExecutionKey, HttpStatusCode.NotFound);

        await whoAmI.GetAsync();
        await Background(whoAmI);
        var gone = await whoAmI.GetAsync();

        gone.Chain[0].ShouldBeSameAs(gone.Annotation);
        gone.Chain[0].DeletedAt.ShouldBe(_fixture.Time.GetUtcNow());
        gone.Chain.Skip(1).ShouldBe(chain.Skip(1));
        gone.GetEffectiveTimeZone().ShouldBe("Europe/Prague");
        first.Chain.ShouldBeSameAs(chain);
        chain[0].DeletedAt.ShouldBeNull();
    }

    // Every annotation of the chain sets a zone of its own; then they lose it one by one, the nearest first.
    [Theory]
    [InlineData(UnitOfExecution, new[] { UnitOfExecution, HostingFixture.ExecutionKey, Context, Usage, Unit, Subject, Responsibility })]
    [InlineData(HostingFixture.ExecutionKey, new[] { HostingFixture.ExecutionKey, Context, Usage, Subject, Responsibility })]
    [InlineData(Usage, new[] { Usage, Subject, Responsibility })]
    [InlineData(Context, new[] { Context, Subject })]
    [InlineData(Unit, new[] { Unit, Responsibility })]
    [InlineData(Responsibility, new[] { Responsibility })]
    [InlineData(Subject, new[] { Subject })]
    public async Task TheEffectiveTimeZone_IsTheFirstOneSetAlongTheChain(string annotationKey, string[] chain)
    {
        _fixture.Environment["STORYTELLER_KEY"] = Full(annotationKey);
        var whoAmI = WhoAmI();

        for (var without = 0; without <= chain.Length; without++)
        {
            RouteZones(chain, without);

            var context = await whoAmI.RefreshAsync();

            context.GetEffectiveTimeZone().ShouldBe(without < chain.Length ? Zones[without] : null);
        }
    }

    [Fact]
    public async Task TheEffectiveTimeZone_SkipsAnUnreadableAncestor_AndReadsNothing()
    {
        _fixture.RouteExecution();
        _fixture.RouteStatus(Context, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson);
        _fixture.RouteAnnotation(Usage, json => json["TimeZone"] = "America/New_York");
        _fixture.RouteAnnotation(Subject, json => json["TimeZone"] = "Europe/Oslo");
        var context = await WhoAmI().GetAsync();
        var requests = _fixture.Http.Requests.Count;

        var zone = context.GetEffectiveTimeZone();

        zone.ShouldBe("America/New_York");
        context.Unreadable.ShouldBe([Full(Context)]);
        _fixture.Http.Requests.Count.ShouldBe(requests);
    }

    // A machine restricted to its execution reads no ancestor, so their zones do not reach it.
    [Fact]
    public async Task ARestrictedMachine_WithoutAZoneOfItsOwn_HasNoEffectiveTimeZone()
    {
        RouteRestrictedMachine();

        var context = await WhoAmI().GetAsync();

        context.Chain.ShouldHaveSingleItem().ShouldBeSameAs(context.Annotation);
        context.GetEffectiveTimeZone().ShouldBeNull();
    }

    // The mapper gives no blank zone, but a snapshot that an application built may carry one: it is not set. The
    // zone that is set comes back as it is.
    [Fact]
    public void TheEffectiveTimeZone_SkipsABlankOne_AndIsAsStored()
    {
        var execution = Snapshot(HostingFixture.ExecutionKey, "   ");
        var context = new StorytellerExecutionContext
        {
            Key = execution.Key,
            Annotation = execution,
            Ancestors = new Dictionary<AnnotationType, StorytellerAnnotation>
            {
                [AnnotationType.Context] = Snapshot(Context, string.Empty),
                [AnnotationType.Usage] = Snapshot(Usage, " Europe/Prague "),
                [AnnotationType.Subject] = Snapshot(Subject, "Europe/Oslo"),
            },
            ReadAt = _fixture.Time.GetUtcNow(),
        };

        context.GetEffectiveTimeZone().ShouldBe(" Europe/Prague ");
    }

    // The chain scope counts every ancestor; the subscription scope counts none of these three.
    [Theory]
    [InlineData(Subject, "IsDisabled", true, StorytellerSubscriptionStatus.Disabled)]
    [InlineData(Context, "ValidFrom", "2026-11-01T00:00:00Z", StorytellerSubscriptionStatus.Scheduled)]
    [InlineData(Responsibility, "ExpiresAt", "2026-10-01T00:00:00Z", StorytellerSubscriptionStatus.Expired)]
    public async Task WithTheChainScope_AnInactiveAncestor_DecidesTheStatus(string ancestor, string member, object value, StorytellerSubscriptionStatus status)
    {
        _fixture.RouteExecution();
        _fixture.RouteAnnotation(ancestor, json => json[member] = JToken.FromObject(value));

        var context = await WhoAmI().GetAsync();

        context.GetSubscriptionStatus(Now, StorytellerStatusScope.Chain).ShouldBe(status);
        context.FindInactiveAnnotation(Now, StorytellerStatusScope.Chain).ShouldNotBeNull().AnnotationKey.ShouldBe(ancestor);
        context.GetSubscriptionStatus(Now, StorytellerStatusScope.Subscription).ShouldBe(StorytellerSubscriptionStatus.Active);
        context.FindInactiveAnnotation(Now, StorytellerStatusScope.Subscription).ShouldBeNull();
        context.GetSubscriptionStatus(Now).ShouldBe(StorytellerSubscriptionStatus.Active);
    }

    [Fact]
    public async Task FindInactiveAnnotation_GivesTheSnapshotOfTheContext_ForALogThatNamesIt()
    {
        _fixture.RouteExecution();
        _fixture.RouteAnnotation(Subject, json => json["IsDisabled"] = true);
        var context = await WhoAmI().GetAsync();
        var requests = _fixture.Http.Requests.Count;

        var inactive = context.FindInactiveAnnotation(Now, StorytellerStatusScope.Chain).ShouldNotBeNull();

        inactive.ShouldBeSameAs(context.Subject);
        $"{context.Annotation.AnnotationKey} is not run: {inactive.AnnotationKey} is {inactive.GetStatus(Now).ToString().ToLowerInvariant()}"
            .ShouldBe("exe.northwind.invoicing.prod is not run: sbt.northwind is disabled");
        _fixture.Http.Requests.Count.ShouldBe(requests);
    }

    [Theory]
    [InlineData(-1, StorytellerSubscriptionStatus.Scheduled)]
    [InlineData(0, StorytellerSubscriptionStatus.Active)]
    public async Task WithTheChainScope_TheStatus_FollowsValidFromOfTheSubject(int seconds, StorytellerSubscriptionStatus status)
    {
        var validFrom = Now.AddHours(1);
        _fixture.RouteExecution();
        _fixture.RouteAnnotation(Subject, json => json["ValidFrom"] = validFrom.ToString("O"));

        var context = await WhoAmI().GetAsync();

        context.GetSubscriptionStatus(validFrom.AddSeconds(seconds), StorytellerStatusScope.Chain).ShouldBe(status);
        (context.FindInactiveAnnotation(validFrom.AddSeconds(seconds), StorytellerStatusScope.Chain) is null).ShouldBe(status == StorytellerSubscriptionStatus.Active);
    }

    // In the chain order: the key's own, then the context before the usage, and the usage before the subject.
    [Fact]
    public async Task WithTheChainScope_TheNearestInactiveAnnotation_Wins()
    {
        _fixture.RouteExecution();
        _fixture.RouteAnnotation(Subject, json => json["IsDisabled"] = true);
        _fixture.RouteAnnotation(Usage, json => json["ExpiresAt"] = "2026-10-01T00:00:00Z");
        var whoAmI = WhoAmI();

        var context = await whoAmI.GetAsync();

        context.GetSubscriptionStatus(Now, StorytellerStatusScope.Chain).ShouldBe(StorytellerSubscriptionStatus.Expired);
        context.FindInactiveAnnotation(Now, StorytellerStatusScope.Chain).ShouldBeSameAs(context.Usage);

        _fixture.RouteAnnotation(Context, json => json["ValidFrom"] = "2026-11-01T00:00:00Z");
        context = await whoAmI.RefreshAsync();

        context.GetSubscriptionStatus(Now, StorytellerStatusScope.Chain).ShouldBe(StorytellerSubscriptionStatus.Scheduled);
        context.FindInactiveAnnotation(Now, StorytellerStatusScope.Chain).ShouldBeSameAs(context.Context);
        context.GetSubscriptionStatus(Now, StorytellerStatusScope.Subscription).ShouldBe(StorytellerSubscriptionStatus.Expired);
        context.FindInactiveAnnotation(Now, StorytellerStatusScope.Subscription).ShouldBeSameAs(context.Usage);

        _fixture.RouteAnnotation(HostingFixture.ExecutionKey, json => json["IsDisabled"] = true);
        context = await whoAmI.RefreshAsync();

        context.GetSubscriptionStatus(Now, StorytellerStatusScope.Chain).ShouldBe(StorytellerSubscriptionStatus.Disabled);
        context.FindInactiveAnnotation(Now, StorytellerStatusScope.Chain).ShouldBeSameAs(context.Annotation);
    }

    // With the subscription scope a key without an execution has the status of its own annotation, and a key with one
    // counts its execution and its usage. With the chain scope a key of every type counts all its ancestors.
    [Theory]
    [InlineData(UnitOfExecution, Unit)]
    [InlineData(UnitOfExecution, Context)]
    [InlineData(UnitOfExecution, Subject)]
    [InlineData(HostingFixture.ExecutionKey, Responsibility)]
    [InlineData(Usage, Subject)]
    [InlineData(Usage, Responsibility)]
    [InlineData(Context, Subject)]
    [InlineData(Unit, Responsibility)]
    public async Task WithTheChainScope_AKeyOfEveryType_CountsItsAncestors(string annotationKey, string disabled)
    {
        RouteKey(annotationKey);
        _fixture.RouteAnnotation(disabled, json => json["IsDisabled"] = true);

        var context = await WhoAmI().GetAsync();

        context.GetSubscriptionStatus(Now, StorytellerStatusScope.Chain).ShouldBe(StorytellerSubscriptionStatus.Disabled);
        context.FindInactiveAnnotation(Now, StorytellerStatusScope.Chain).ShouldNotBeNull().AnnotationKey.ShouldBe(disabled);
        context.GetSubscriptionStatus(Now, StorytellerStatusScope.Subscription).ShouldBe(StorytellerSubscriptionStatus.Active);
        context.FindInactiveAnnotation(Now, StorytellerStatusScope.Subscription).ShouldBeNull();
    }

    [Theory]
    [InlineData(Responsibility)]
    [InlineData(Subject)]
    public async Task WithTheChainScope_AKeyWithoutAncestors_HasTheStatusOfItsOwnAnnotation(string annotationKey)
    {
        RouteKey(annotationKey);
        _fixture.RouteAnnotation(annotationKey, json => json["ExpiresAt"] = "2026-10-01T00:00:00Z");

        var context = await WhoAmI().GetAsync();

        context.GetSubscriptionStatus(Now, StorytellerStatusScope.Chain).ShouldBe(StorytellerSubscriptionStatus.Expired);
        context.FindInactiveAnnotation(Now, StorytellerStatusScope.Chain).ShouldBeSameAs(context.Annotation);
        context.GetSubscriptionStatus(Now, StorytellerStatusScope.Subscription).ShouldBe(StorytellerSubscriptionStatus.Expired);
        context.FindInactiveAnnotation(Now, StorytellerStatusScope.Subscription).ShouldBeSameAs(context.Annotation);
    }

    [Theory]
    [InlineData(HostingFixture.ExecutionKey)]
    [InlineData(UnitOfExecution)]
    [InlineData(Usage)]
    [InlineData(Subject)]
    public async Task WhenEverythingIsActive_BothScopesAreActive_AndNoAnnotationIsFound(string annotationKey)
    {
        RouteKey(annotationKey);

        var context = await WhoAmI().GetAsync();

        foreach (var scope in Enum.GetValues<StorytellerStatusScope>())
        {
            context.GetSubscriptionStatus(Now, scope).ShouldBe(StorytellerSubscriptionStatus.Active);
            context.FindInactiveAnnotation(Now, scope).ShouldBeNull();
        }
    }

    // Any ancestor: the subscription scope asks about the usage only, and about the execution of a unit of execution.
    [Theory]
    [InlineData(HostingFixture.ExecutionKey, Subject)]
    [InlineData(HostingFixture.ExecutionKey, Context)]
    [InlineData(UnitOfExecution, Unit)]
    [InlineData(Usage, Responsibility)]
    [InlineData(Context, Subject)]
    public async Task WithTheChainScope_AnUnreadableAncestor_IsUnknown_WhileTheRestIsActive(string annotationKey, string restricted)
    {
        RouteKey(annotationKey);
        _fixture.RouteStatus(restricted, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson);

        var context = await WhoAmI().GetAsync();

        context.Unreadable.ShouldBe([Full(restricted)]);
        context.GetSubscriptionStatus(Now, StorytellerStatusScope.Chain).ShouldBe(StorytellerSubscriptionStatus.Unknown);
        context.FindInactiveAnnotation(Now, StorytellerStatusScope.Chain).ShouldBeNull();
        context.GetSubscriptionStatus(Now, StorytellerStatusScope.Subscription).ShouldBe(StorytellerSubscriptionStatus.Active);
    }

    [Fact]
    public async Task WithTheChainScope_AnUnreadableAncestor_DoesNotHideAnInactiveOne()
    {
        _fixture.RouteExecution();
        _fixture.RouteStatus(Subject, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson);
        _fixture.RouteAnnotation(Responsibility, json => json["IsDisabled"] = true);

        var context = await WhoAmI().GetAsync();

        context.GetSubscriptionStatus(Now, StorytellerStatusScope.Chain).ShouldBe(StorytellerSubscriptionStatus.Disabled);
        context.FindInactiveAnnotation(Now, StorytellerStatusScope.Chain).ShouldBeSameAs(context.Responsibility);
    }

    // A machine restricted to its execution: unknown in both scopes, and no annotation decides it.
    [Fact]
    public async Task ARestrictedMachine_IsUnknownInBothScopes_AndNoAnnotationIsFound()
    {
        RouteRestrictedMachine();

        var context = await WhoAmI().GetAsync();

        foreach (var scope in Enum.GetValues<StorytellerStatusScope>())
        {
            context.GetSubscriptionStatus(Now, scope).ShouldBe(StorytellerSubscriptionStatus.Unknown);
            context.FindInactiveAnnotation(Now, scope).ShouldBeNull();
        }
    }

    // A missing ancestor was deleted after the key was read: it is in neither Ancestors nor Unreadable, and counts in no
    // scope.
    [Fact]
    public async Task WithTheChainScope_AMissingAncestor_DoesNotCount()
    {
        _fixture.RouteExecution();
        _fixture.RouteStatus(Context, HttpStatusCode.NotFound);

        var context = await WhoAmI().GetAsync();

        context.GetSubscriptionStatus(Now, StorytellerStatusScope.Chain).ShouldBe(StorytellerSubscriptionStatus.Active);
        context.FindInactiveAnnotation(Now, StorytellerStatusScope.Chain).ShouldBeNull();
    }

    [Fact]
    public async Task ADeletedKey_IsDeletedInBothScopes_BeforeAnInactiveAncestor_AndIsTheAnnotationFound()
    {
        _fixture.RouteExecution();
        _fixture.RouteAnnotation(HostingFixture.ExecutionKey, TestAnnotations.Deleted());
        _fixture.RouteAnnotation(Subject, json => json["IsDisabled"] = true);
        _fixture.RouteStatus(Context, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson);

        var context = await WhoAmI().GetAsync();

        foreach (var scope in Enum.GetValues<StorytellerStatusScope>())
        {
            context.GetSubscriptionStatus(Now, scope).ShouldBe(StorytellerSubscriptionStatus.Deleted);
            context.FindInactiveAnnotation(Now, scope).ShouldBeSameAs(context.Annotation);
        }
    }

    // A read leaves a deleted ancestor out, and a background read that finds the key gone deletes the key's own
    // annotation, so only a context that an application built holds a deleted execution next to a key that is not.
    [Fact]
    public void ADeletedExecution_OfAUnitOfExecution_IsDeletedInBothScopes_AndIsTheAnnotationFound()
    {
        var execution = DeletedExecution(Now.AddDays(-1));
        var context = new StorytellerExecutionContext
        {
            Key = Full(UnitOfExecution),
            Annotation = Snapshot(UnitOfExecution),
            Ancestors = new Dictionary<AnnotationType, StorytellerAnnotation>
            {
                [AnnotationType.Execution] = execution,
                [AnnotationType.Unit] = Snapshot(Unit, isDisabled: true),
            },
            ReadAt = Now,
        };

        foreach (var scope in Enum.GetValues<StorytellerStatusScope>())
        {
            context.GetSubscriptionStatus(Now, scope).ShouldBe(StorytellerSubscriptionStatus.Deleted);
            context.FindInactiveAnnotation(Now, scope).ShouldBeSameAs(execution);
        }
    }

    // The chain scope counts the execution that the chain holds, so a base snapshot of it deletes the key as it would
    // disable it. The subscription scope knows the typed execution only, as the status without a scope always did.
    [Fact]
    public void ADeletedExecution_ThatIsABaseSnapshot_IsDeletedInTheChainScope_AndDoesNotCountInTheSubscriptionScope()
    {
        var execution = new StorytellerAnnotation
        {
            Key = Full(HostingFixture.ExecutionKey),
            AnnotationKey = HostingFixture.ExecutionKey,
            Type = AnnotationType.Execution,
            Name = "prod",
            DeletedAt = Now.AddDays(-1),
        };
        var context = new StorytellerExecutionContext
        {
            Key = Full(UnitOfExecution),
            Annotation = Snapshot(UnitOfExecution),
            Ancestors = new Dictionary<AnnotationType, StorytellerAnnotation> { [AnnotationType.Execution] = execution },
            ReadAt = Now,
        };

        context.Execution.ShouldBeNull();
        context.GetSubscriptionStatus(Now, StorytellerStatusScope.Chain).ShouldBe(StorytellerSubscriptionStatus.Deleted);
        context.FindInactiveAnnotation(Now, StorytellerStatusScope.Chain).ShouldBeSameAs(execution);
        context.GetSubscriptionStatus(Now, StorytellerStatusScope.Subscription).ShouldBe(StorytellerSubscriptionStatus.Active);
        context.FindInactiveAnnotation(Now, StorytellerStatusScope.Subscription).ShouldBeNull();
        context.GetSubscriptionStatus(Now).ShouldBe(StorytellerSubscriptionStatus.Active);
    }

    // The key's own annotation is named first when both are deleted.
    [Fact]
    public void ADeletedUnitOfExecution_WithADeletedExecution_FindsItsOwnAnnotation()
    {
        var unitOfExecution = new StorytellerAnnotation
        {
            Key = Full(UnitOfExecution),
            AnnotationKey = UnitOfExecution,
            Type = AnnotationType.UnitOfExecution,
            Name = "daily",
            DeletedAt = Now.AddHours(-1),
        };
        var context = new StorytellerExecutionContext
        {
            Key = unitOfExecution.Key,
            Annotation = unitOfExecution,
            Ancestors = new Dictionary<AnnotationType, StorytellerAnnotation> { [AnnotationType.Execution] = DeletedExecution(Now.AddDays(-1)) },
            ReadAt = Now,
        };

        foreach (var scope in Enum.GetValues<StorytellerStatusScope>())
        {
            context.GetSubscriptionStatus(Now, scope).ShouldBe(StorytellerSubscriptionStatus.Deleted);
            context.FindInactiveAnnotation(Now, scope).ShouldBeSameAs(unitOfExecution);
        }
    }

    // The subscription scope is the rule of GetSubscriptionStatus(now), and names the annotation that decides it: the
    // first one of the key's annotation, its execution and its usage.
    [Theory]
    [InlineData(HostingFixture.ExecutionKey, HostingFixture.ExecutionKey, StorytellerSubscriptionStatus.Disabled, HostingFixture.ExecutionKey)]
    [InlineData(HostingFixture.ExecutionKey, Usage, StorytellerSubscriptionStatus.Disabled, Usage)]
    [InlineData(HostingFixture.ExecutionKey, Context, StorytellerSubscriptionStatus.Active, null)]
    [InlineData(HostingFixture.ExecutionKey, Subject, StorytellerSubscriptionStatus.Active, null)]
    [InlineData(UnitOfExecution, UnitOfExecution, StorytellerSubscriptionStatus.Disabled, UnitOfExecution)]
    [InlineData(UnitOfExecution, HostingFixture.ExecutionKey, StorytellerSubscriptionStatus.Disabled, HostingFixture.ExecutionKey)]
    [InlineData(UnitOfExecution, Usage, StorytellerSubscriptionStatus.Disabled, Usage)]
    [InlineData(UnitOfExecution, Unit, StorytellerSubscriptionStatus.Active, null)]
    [InlineData(Usage, Usage, StorytellerSubscriptionStatus.Disabled, Usage)]
    [InlineData(Usage, Subject, StorytellerSubscriptionStatus.Active, null)]
    [InlineData(Unit, Unit, StorytellerSubscriptionStatus.Disabled, Unit)]
    [InlineData(Unit, Responsibility, StorytellerSubscriptionStatus.Active, null)]
    public async Task TheSubscriptionScope_IsTheStatusWithoutAScope_AndFindsTheAnnotationThatDecidesIt(string annotationKey, string disabled, StorytellerSubscriptionStatus status, string? inactive)
    {
        RouteKey(annotationKey);
        _fixture.RouteAnnotation(disabled, json => json["IsDisabled"] = true);

        var context = await WhoAmI().GetAsync();

        context.GetSubscriptionStatus(Now).ShouldBe(status);
        context.GetSubscriptionStatus(Now, StorytellerStatusScope.Subscription).ShouldBe(status);
        (context.FindInactiveAnnotation(Now, StorytellerStatusScope.Subscription)?.AnnotationKey).ShouldBe(inactive);
    }

    // The execution of the key comes before its usage, in both scopes.
    [Fact]
    public async Task TheSubscriptionScope_FindsTheExecution_BeforeTheUsage()
    {
        RouteUnitOfExecution();
        _fixture.RouteAnnotation(HostingFixture.ExecutionKey, json => json["ValidFrom"] = "2026-11-01T00:00:00Z");
        _fixture.RouteAnnotation(Usage, json => json["IsDisabled"] = true);

        var context = await WhoAmI().GetAsync();

        foreach (var scope in Enum.GetValues<StorytellerStatusScope>())
        {
            context.GetSubscriptionStatus(Now, scope).ShouldBe(StorytellerSubscriptionStatus.Scheduled);
            context.FindInactiveAnnotation(Now, scope).ShouldBeSameAs(context.Execution);
        }
    }

    [Fact]
    public async Task AScopeThatIsNotDefined_IsRefused()
    {
        _fixture.RouteExecution();
        _fixture.RouteAnnotation(HostingFixture.ExecutionKey, TestAnnotations.Deleted());
        var context = await WhoAmI().GetAsync();
        var scope = (StorytellerStatusScope)2;

        Should.Throw<ArgumentOutOfRangeException>(() => context.GetSubscriptionStatus(Now, scope)).ParamName.ShouldBe("scope");
        Should.Throw<ArgumentOutOfRangeException>(() => context.FindInactiveAnnotation(Now, scope)).ParamName.ShouldBe("scope");
    }

    // A test double of who am I has the chain status too. An entry of Unreadable counts when it names an annotation of
    // a type that the context does not hold.
    [Fact]
    public void AContextThatAnApplicationBuilt_HasItsChainStatus()
    {
        var usage = Snapshot(Usage);
        var subject = Snapshot(Subject, isDisabled: true);

        var active = Build([Full(Usage), "not a key"]);
        var unknown = Build([Full(Responsibility)]);
        var disabled = Build([Full(Responsibility)], subject);

        active.GetSubscriptionStatus(Now, StorytellerStatusScope.Chain).ShouldBe(StorytellerSubscriptionStatus.Active);
        unknown.GetSubscriptionStatus(Now, StorytellerStatusScope.Chain).ShouldBe(StorytellerSubscriptionStatus.Unknown);
        unknown.FindInactiveAnnotation(Now, StorytellerStatusScope.Chain).ShouldBeNull();
        unknown.GetSubscriptionStatus(Now, StorytellerStatusScope.Subscription).ShouldBe(StorytellerSubscriptionStatus.Active);
        disabled.GetSubscriptionStatus(Now, StorytellerStatusScope.Chain).ShouldBe(StorytellerSubscriptionStatus.Disabled);
        disabled.FindInactiveAnnotation(Now, StorytellerStatusScope.Chain).ShouldBeSameAs(subject);
        disabled.GetSubscriptionStatus(Now, StorytellerStatusScope.Subscription).ShouldBe(StorytellerSubscriptionStatus.Active);

        StorytellerExecutionContext Build(string[] unreadable, StorytellerAnnotation? ancestor = null)
        {
            var ancestors = new Dictionary<AnnotationType, StorytellerAnnotation>();

            if (ancestor is not null)
            {
                ancestors[ancestor.Type] = ancestor;
            }

            return new StorytellerExecutionContext
            {
                Key = usage.Key,
                Annotation = usage,
                Ancestors = ancestors,
                Unreadable = unreadable,
                ReadAt = Now,
            };
        }
    }

    // What counts of Unreadable in a context that an application built, a usage with its subject: a full key of a type
    // that the context holds no annotation of, the type of a key that is no ancestor of the usage too. An annotation
    // key, a text that is no key, and a full key of the key's own type or of a type in Ancestors do not count. A read
    // puts only the full keys of the ancestors that it could not read there.
    [Theory]
    [InlineData(HostingFixture.Scope + "." + Responsibility, StorytellerSubscriptionStatus.Unknown)]
    [InlineData(HostingFixture.Scope + "." + Unit, StorytellerSubscriptionStatus.Unknown)]
    [InlineData(HostingFixture.Scope + "." + Usage, StorytellerSubscriptionStatus.Active)]
    [InlineData(HostingFixture.Scope + "." + Subject, StorytellerSubscriptionStatus.Active)]
    [InlineData(Responsibility, StorytellerSubscriptionStatus.Active)]
    [InlineData("not a key", StorytellerSubscriptionStatus.Active)]
    public void AContextThatAnApplicationBuilt_CountsAnUnreadableEntry_ThatIsAFullKeyOfATypeItDoesNotHold(string unreadable, StorytellerSubscriptionStatus status)
    {
        var usage = Snapshot(Usage);
        var context = new StorytellerExecutionContext
        {
            Key = usage.Key,
            Annotation = usage,
            Ancestors = new Dictionary<AnnotationType, StorytellerAnnotation> { [AnnotationType.Subject] = Snapshot(Subject) },
            Unreadable = [unreadable],
            ReadAt = Now,
        };

        context.GetSubscriptionStatus(Now, StorytellerStatusScope.Chain).ShouldBe(status);
        context.FindInactiveAnnotation(Now, StorytellerStatusScope.Chain).ShouldBeNull();
        context.GetSubscriptionStatus(Now, StorytellerStatusScope.Subscription).ShouldBe(StorytellerSubscriptionStatus.Active);
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }

    private static string Full(string annotationKey)
    {
        return $"{HostingFixture.Scope}.{annotationKey}";
    }

    private static Task Background(IExecutionContext whoAmI)
    {
        return ((ExecutionContextService)whoAmI).BackgroundRead.WaitAsync(HostingFixture.Patience);
    }

    // A base snapshot, as an application builds one for a test double of who am I.
    private static StorytellerAnnotation Snapshot(string annotationKey, string? timeZone = null, bool isDisabled = false)
    {
        var key = AnnotationKey.Parse(annotationKey);
        return new StorytellerAnnotation { Key = Full(annotationKey), AnnotationKey = annotationKey, Type = key.Type, Name = key.Name, TimeZone = timeZone, IsDisabled = isDisabled };
    }

    // The typed snapshot of the execution, deleted: what the members of who am I count as the execution of a key.
    private static StorytellerExecution DeletedExecution(DateTimeOffset deletedAt)
    {
        return new StorytellerExecution
        {
            Key = Full(HostingFixture.ExecutionKey),
            AnnotationKey = HostingFixture.ExecutionKey,
            Type = AnnotationType.Execution,
            Name = "prod",
            SubjectName = "northwind",
            ResponsibilityName = "invoicing",
            ContextName = "prod",
            DeletedAt = deletedAt,
        };
    }

    // The key of a unit of execution and its six ancestors, all active.
    private void RouteUnitOfExecution()
    {
        RouteKey(UnitOfExecution);
    }

    // A key of any type and its ancestors, all active.
    private void RouteKey(string annotationKey)
    {
        _fixture.Environment["STORYTELLER_KEY"] = Full(annotationKey);
        _fixture.RouteExecution(annotationKey);
    }

    // The annotations of a chain, each with the zone of its place in it, but the first ones, which have none.
    private void RouteZones(string[] chain, int without)
    {
        for (var index = 0; index < chain.Length; index++)
        {
            var zone = Zones[index];
            _fixture.RouteAnnotation(chain[index], index < without ? null : json => json["TimeZone"] = zone);
        }
    }

    // A machine restricted to its execution: every ancestor answers 403 AnnotationRestricted.
    private void RouteRestrictedMachine()
    {
        _fixture.RouteAnnotation(HostingFixture.ExecutionKey);

        foreach (var ancestor in new[] { Subject, Responsibility, Usage, Context })
        {
            _fixture.RouteStatus(ancestor, HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson);
        }
    }

    private List<RecordingLoggerFactory.LogEntry> InformationLogs()
    {
        return _fixture.Logs.Entries.Where(entry => entry.Category == LogCategory && entry.Level == LogLevel.Information).ToList();
    }

    private IExecutionContext WhoAmI(TimeSpan? refreshInterval = null)
    {
        return _fixture.BuildServices(services: services => services.Configure<StorytellerExecutionContextOptions>(options => options.RefreshInterval = refreshInterval ?? RefreshInterval))
            .GetRequiredService<IExecutionContext>();
    }
}
