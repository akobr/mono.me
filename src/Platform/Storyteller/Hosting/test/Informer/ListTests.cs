using System.Net;
using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Hosting.Informer;
using _42.Platform.Storyteller.Sdk.Hosting.Reading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Informer;

// The two lists of a poll: their tokens and guards, over a script and over HTTP.
public sealed class ListTests : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly HostingFixture _fixture = new();
    private readonly ScriptedPages _pages = new();

    [Fact]
    public async Task TheLists_FollowTheirTokens_ThroughAnEmptyPage()
    {
        _pages.Pages = (list, token) => (list, token) switch
        {
            (AnnotationList.Executions, null) => ScriptedPages.Page("a"),
            (AnnotationList.Executions, "a") => ScriptedPages.Page("b", ScriptedPages.Item(FakeCatalog.ExecutionKey("northwind"))),
            (AnnotationList.Executions, "b") => ScriptedPages.Page(null, ScriptedPages.Item(FakeCatalog.ExecutionKey("contoso"))),
            (AnnotationList.Usages, null) => ScriptedPages.Page(null, ScriptedPages.Item(FakeCatalog.UsageKey("northwind")), ScriptedPages.Item(FakeCatalog.UsageKey("contoso"))),
            _ => throw new InvalidOperationException($"No page {list} {token}."),
        };

        var batch = (await ReadOnceAsync()).ShouldHaveSingleItem();

        batch.Failure.ShouldBeNull();
        batch.Changes.Select(change => (change.Kind, change.Execution.SubjectName)).ShouldBe(
            [(StorytellerChangeKind.Added, "contoso"), (StorytellerChangeKind.Added, "northwind")]);
        _pages.PageReads.Where(read => read.List == AnnotationList.Executions).Select(read => read.Token).ShouldBe([null, "a", "b"]);
    }

    [Fact]
    public async Task AKeyListedTwice_KeepsTheItemListedLast()
    {
        var execution = FakeCatalog.ExecutionKey("northwind");
        var isFirstPoll = true;
        _pages.Pages = (list, token) => (list, token, isFirstPoll) switch
        {
            (AnnotationList.Executions, null, true) => ScriptedPages.Page("a", ScriptedPages.Item(execution, "2026-10-20T08:00:00Z")),
            (AnnotationList.Executions, "a", true) => ScriptedPages.Page(null, ScriptedPages.Item(execution, "2026-10-21T08:00:00Z")),
            (AnnotationList.Executions, null, false) => ScriptedPages.Page(null, ScriptedPages.Item(execution, "2026-10-21T08:00:00Z")),
            (AnnotationList.Usages, null, _) => ScriptedPages.Page(null, ScriptedPages.Item(FakeCatalog.UsageKey("northwind"))),
            _ => throw new InvalidOperationException($"No page {list} {token}."),
        };
        await using var probe = Probe();

        (await probe.FirstPollAsync()).ShouldHaveSingleItem().Changes.ShouldHaveSingleItem().Kind.ShouldBe(StorytellerChangeKind.Added);
        isFirstPoll = false;
        var reads = _pages.PointReads.Count;

        // The item listed first would read as changed now.
        (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem().Changes.ShouldBeEmpty();
        _pages.PointReads.Count.ShouldBe(reads);
    }

    [Fact]
    public async Task ATokenAnsweredTwice_FailsThePoll_NamingTheServerFix()
    {
        _pages.Pages = (list, token) => list == AnnotationList.Executions ? ScriptedPages.Page("a") : ScriptedPages.Page(null);

        var batch = (await ReadOnceAsync()).ShouldHaveSingleItem();

        var failure = batch.Failure.ShouldNotBeNull();
        failure.Message.ShouldBe(
            "Listing the executions of @acme.billing.default.rst.invoicing did not end: the platform answered a continuation token it had answered before. A Storyteller API without the fix that pages annotation lists through their continuation tokens answers the first page again; update the API.");
        failure.Key.ShouldBe("@acme.billing.default.rst.invoicing");
        failure.IsTransient.ShouldBeFalse();
        batch.Changes.ShouldBeEmpty();
        _pages.PageReads.Count(read => read.List == AnnotationList.Executions).ShouldBe(2);
    }

    [Fact]
    public async Task AListOfMoreThanAThousandPages_FailsThePoll()
    {
        _pages.Pages = (list, token) => list == AnnotationList.Usages
            ? ScriptedPages.Page(null)
            : ScriptedPages.Page((int.Parse(token ?? "0", System.Globalization.CultureInfo.InvariantCulture) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));

        var batch = (await ReadOnceAsync()).ShouldHaveSingleItem();

        batch.Failure.ShouldNotBeNull().Message.ShouldStartWith(
            "Listing the executions of @acme.billing.default.rst.invoicing did not end: it had more than 1000 pages. A Storyteller API without the fix");
        _pages.PageReads.Count(read => read.List == AnnotationList.Executions).ShouldBe(PollingSubscriptionSource.MaxPages);
    }

    [Fact]
    public async Task ABatch_MarksEveryHeldKeyDirty_AndAFailedPollYieldsItsFailureOnly()
    {
        var failing = false;
        _pages.Pages = (list, _) =>
        {
            if (failing && list == AnnotationList.Usages)
            {
                throw new StorytellerConfigurationException("The usages did not come.", "@acme.billing.default.rst.invoicing", 503, null, true);
            }

            return list == AnnotationList.Executions
                ? ScriptedPages.Page(null, ScriptedPages.Item(FakeCatalog.ExecutionKey("northwind")), ScriptedPages.Item(FakeCatalog.ExecutionKey("contoso")))
                : ScriptedPages.Page(null, ScriptedPages.Item(FakeCatalog.UsageKey("northwind")), ScriptedPages.Item(FakeCatalog.UsageKey("contoso")));
        };
        await using var probe = Probe();

        (await probe.FirstPollAsync()).ShouldHaveSingleItem().DirtyKeys.ShouldBe(
            [FakeCatalog.Full(FakeCatalog.ExecutionKey("contoso")), FakeCatalog.Full(FakeCatalog.ExecutionKey("northwind"))]);
        (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem().DirtyKeys.Count.ShouldBe(2);

        failing = true;
        var failed = (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem();
        failed.Failure.ShouldNotBeNull().StatusCode.ShouldBe(503);
        failed.Changes.ShouldBeEmpty();
        failed.DirtyKeys.ShouldBeEmpty();

        failing = false;
        var healed = (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem();
        healed.Changes.ShouldBeEmpty();
        healed.DirtyKeys.Count.ShouldBe(2);
    }

    [Fact]
    public async Task OverHttp_TheListsFollowEscapedTokens_WithoutFlags()
    {
        var catalog = new FakeCatalog(_fixture) { PageSize = 1 };
        catalog.Subscribe("northwind");
        catalog.Subscribe("contoso");
        catalog.Subscribe("fabrikam");

        var changes = await WatchOnceAsync();

        changes.Select(change => change.Execution.SubjectName).ShouldBe(["contoso", "fabrikam", "northwind"]);
        catalog.Lists("executions").Select(request => request.Uri.Query).ShouldBe(
            [string.Empty, $"?continuationToken={Uri.EscapeDataString(FakeCatalog.Token("executions", 1))}", $"?continuationToken={Uri.EscapeDataString(FakeCatalog.Token("executions", 2))}"]);
        catalog.Lists("usages").Count.ShouldBe(3);
    }

    [Fact]
    public async Task OverHttp_AFailedPage_FailsThePoll_AndTheHeldSetStays()
    {
        var catalog = new FakeCatalog(_fixture) { PageSize = 1 };
        catalog.Subscribe("northwind");
        catalog.Subscribe("contoso");
        await using var probe = new PollProbe<StorytellerExecutionChange>(Catalog().WatchExecutionsAsync(FakeCatalog.Responsibility, FakeCatalog.Context, Interval), _fixture.Time);
        (await probe.FirstPollAsync()).Count.ShouldBe(2);

        catalog.FailPage("usages", FakeCatalog.Token("usages", 1), HttpStatusCode.ServiceUnavailable, TestAnnotations.ErrorJson);
        catalog.Purge(FakeCatalog.ExecutionKey("contoso"));
        (await probe.NextPollAsync(Interval)).ShouldBeEmpty();

        var warning = _fixture.Logs.Entries.Where(entry => entry.Level >= LogLevel.Warning).ShouldHaveSingleItem();
        warning.Category.ShouldBe("_42.Platform.Storyteller.Sdk.Hosting.Informer");
        warning.Message.ShouldBe(
            "Polling the subscriptions of @acme.billing.default.rst.invoicing in the context prod failed (transient); the 2 subscriptions held stay as they are. Listing the usages of @acme.billing.default.rst.invoicing failed (503 AccessDenied): No. Ask the owner.");

        catalog.Heal();
        var removed = (await probe.NextPollAsync(Interval)).ShouldHaveSingleItem();
        removed.Kind.ShouldBe(StorytellerChangeKind.Removed);
        removed.Execution.SubjectName.ShouldBe("contoso");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson, "The machine may not list the executions of @acme.billing.default.rst.invoicing: a machine restricted to an annotation reads that annotation and its descendants only, so the informer needs one restricted to the responsibility, or an unrestricted one (403 AnnotationRestricted): Restricted.", false)]
    [InlineData(HttpStatusCode.Forbidden, TestAnnotations.ErrorJson, "The machine may not list the executions of @acme.billing.default.rst.invoicing; it needs Annotation.Read on the project (403 AccessDenied): No. Ask the owner.", false)]
    [InlineData(HttpStatusCode.BadRequest, TestAnnotations.ErrorJson, "The platform does not accept the list of the executions of @acme.billing.default.rst.invoicing or its continuation token (400 AccessDenied): No. Ask the owner.", false)]
    [InlineData(HttpStatusCode.OK, "<html>Sign in to the Wi-Fi</html>", "The answer to the list of the executions of @acme.billing.default.rst.invoicing did not parse as a list of annotations (200): check that the endpoint is the Storyteller API base, /api included, and that nothing in between rewrites the response.", true)]
    public async Task AFailedList_ThrowsWithTheListsWords_WhenThereIsOnePoll(HttpStatusCode status, string body, string message, bool isTransient)
    {
        var catalog = new FakeCatalog(_fixture);
        catalog.FailPage("executions", null, status, body);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => WatchOnceAsync());

        exception.Message.ShouldBe(message);
        exception.Key.ShouldBe("@acme.billing.default.rst.invoicing");
        exception.StatusCode.ShouldBe((int)status);
        exception.IsTransient.ShouldBe(isTransient);
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }

    private static SubscriptionScope Scope()
    {
        return SubscriptionScope.Create(FullKey.Parse(HostingFixture.Scope + ".rst.invoicing"), FakeCatalog.Responsibility, FakeCatalog.Context);
    }

    private PollingSubscriptionSource Source(TimeSpan interval)
    {
        return new PollingSubscriptionSource(_pages, interval, 4, _fixture.Time, () => 0.5, NullLogger.Instance);
    }

    private async Task<IReadOnlyList<SubscriptionBatch>> ReadOnceAsync()
    {
        var batches = new List<SubscriptionBatch>();

        await foreach (var batch in Source(TimeSpan.Zero).ReadAsync(Scope(), CancellationToken.None))
        {
            batches.Add(batch);
        }

        return batches;
    }

    private PollProbe<SubscriptionBatch> Probe()
    {
        return new PollProbe<SubscriptionBatch>(Source(Interval).ReadAsync(Scope(), CancellationToken.None), _fixture.Time);
    }

    private ISubscriptionCatalog Catalog()
    {
        return _fixture.BuildServices().GetRequiredService<ISubscriptionCatalog>();
    }

    private async Task<IReadOnlyList<StorytellerExecutionChange>> WatchOnceAsync()
    {
        var changes = new List<StorytellerExecutionChange>();

        await foreach (var change in Catalog().WatchExecutionsAsync(FakeCatalog.Responsibility, FakeCatalog.Context, TimeSpan.Zero))
        {
            changes.Add(change);
        }

        return changes;
    }
}
