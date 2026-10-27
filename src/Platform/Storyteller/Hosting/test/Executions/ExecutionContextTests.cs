using System.Net;
using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Hosting.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Executions;

public sealed class ExecutionContextTests : IDisposable
{
    private const string Subject = "sbt.northwind";
    private const string Responsibility = "rst.invoicing";
    private const string Usage = "usg.northwind.invoicing";
    private const string Context = "cnt.northwind.prod";
    private const string LogCategory = "_42.Platform.Storyteller.Sdk.Hosting.ExecutionContext";

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    private readonly HostingFixture _fixture = new();

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
        const string key = "uxe.northwind.invoicing.prod.daily";
        _fixture.Environment["STORYTELLER_KEY"] = Full(key);
        _fixture.RouteExecution(key);

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
        _fixture.RouteAnnotation(Usage, json => json["Labels"] = new Newtonsoft.Json.Linq.JArray("gold"));

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
    public async Task ADeletedKey_IsAnAnswer_WithTheStatusDeleted()
    {
        _fixture.RouteExecution();
        _fixture.RouteAnnotation(HostingFixture.ExecutionKey, TestAnnotations.Deleted("2026-10-26T10:00:00Z"));

        var context = await WhoAmI().GetAsync();

        context.Annotation.DeletedAt.ShouldBe(DateTimeOffset.Parse("2026-10-26T10:00:00Z"));
        context.GetSubscriptionStatus(_fixture.Time.GetUtcNow()).ShouldBe(StorytellerSubscriptionStatus.Deleted);
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
