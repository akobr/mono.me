using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Text.Json;
using _42.Testing.UI.Automation.Runtime.Reporting;
using _42.Testing.UI.Automation.Runtime.Settings;
using _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Reporting;

public sealed class ReportingDispatcherTests
{
    private readonly FakeTimeProvider time = new(TestData.Start);

    [Fact]
    public async Task Report_SendsFullBatches_AndTheRestOnCompletion()
    {
        var reporter = new FakeReporter();
        await using var dispatcher = Dispatcher([reporter], options => options.BatchSize = 3);
        await dispatcher.StartAsync(TestData.Run(), []);

        for (var index = 0; index < 7; index++)
        {
            dispatcher.Report(TestData.Report($"Test{index}"));
        }

        await Eventually.TrueAsync(() => reporter.Batches.Count == 2, "two full batches are sent");
        await Eventually.SettleAsync();
        reporter.Batches.Select(batch => batch.Count).ShouldBe([3, 3]);

        await dispatcher.CompleteAsync();

        reporter.Batches.Select(batch => batch.Count).ShouldBe([3, 3, 1]);
        reporter.Results.Select(result => result.Test.MethodName).ShouldBe(Enumerable.Range(0, 7).Select(index => $"Test{index}"));
    }

    [Fact]
    public async Task Report_SendsAPartialBatch_AfterTheFlushInterval()
    {
        var reporter = new FakeReporter();
        await using var dispatcher = Dispatcher([reporter], options => options.FlushInterval = TimeSpan.FromSeconds(5));
        await dispatcher.StartAsync(TestData.Run(), []);

        dispatcher.Report(TestData.Report("First"));
        dispatcher.Report(TestData.Report("Second"));
        await Eventually.SettleAsync();
        reporter.Batches.ShouldBeEmpty();

        await AdvanceUntilAsync(() => !reporter.Batches.IsEmpty, TimeSpan.FromSeconds(1), "the interval flushes the batch");

        reporter.Batches.ShouldHaveSingleItem().Count.ShouldBe(2);
    }

    [Fact]
    public async Task AFailingReporter_NeverSurfaces_AndBecomesAWarning()
    {
        var reporter = new FakeReporter("Testiny") { ReportFailure = _ => new InvalidOperationException("The API is gone.") };
        await using var dispatcher = Dispatcher([reporter], options => options.BatchSize = 1);
        await dispatcher.StartAsync(TestData.Run(), []);

        Should.NotThrow(() => dispatcher.Report(TestData.Report("Test")));
        var summary = await dispatcher.CompleteAsync();

        reporter.ReportCalls.ShouldBe(1);
        summary.Passed.ShouldBe(1);
        summary.Warnings.ShouldContain("Testiny: sending 1 result(s) failed: InvalidOperationException: The API is gone.");
        reporter.CompletedSummary.ShouldNotBeNull();
        reporter.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task AReporterThatDoesNotStart_GetsNothingMore()
    {
        var files = new MockFileSystem();
        var resultsFile = MockUnixSupport.Path(@"C:\run\results.jsonl");
        var reporter = new FakeReporter("Testiny") { StartFailure = new UnauthorizedAccessException("Bad API key.") };
        await using var dispatcher = Dispatcher([reporter], options =>
        {
            options.FileSystem = files;
            options.ResultsFilePath = resultsFile;
        });

        await dispatcher.StartAsync(TestData.Run(), []);
        dispatcher.Report(TestData.Report("Test"));
        var summary = await dispatcher.CompleteAsync();

        reporter.ReportCalls.ShouldBe(0);
        reporter.CompletedSummary.ShouldBeNull();
        reporter.Disposed.ShouldBeTrue();
        summary.Warnings.ShouldBe(
        [
            "Testiny: starting the run failed: UnauthorizedAccessException: Bad API key.",
            $"Testiny: nothing is reported to it in this run; the results stay in {resultsFile}.",
        ]);
        files.File.ReadAllLines(resultsFile).Length.ShouldBe(2);
    }

    [Fact]
    public async Task ATransientFailure_IsRetried_WithExponentialBackoff()
    {
        var reporter = new FakeReporter { ReportFailure = call => call <= 2 ? new TransientReporterException("429 Too Many Requests") : null };
        await using var dispatcher = Dispatcher([reporter], options => options.BatchSize = 1);
        await dispatcher.StartAsync(TestData.Run(), []);

        dispatcher.Report(TestData.Report("Test"));
        await Eventually.TrueAsync(() => reporter.ReportCalls == 1, "the first attempt runs");

        // 1 s after the first failure, 2 s after the second.
        await Eventually.SettleAsync();
        time.Advance(TimeSpan.FromSeconds(1));
        await Eventually.TrueAsync(() => reporter.ReportCalls == 2, "the second attempt runs after 1 s");

        await Eventually.SettleAsync();
        time.Advance(TimeSpan.FromSeconds(1));
        await Eventually.SettleAsync();
        reporter.ReportCalls.ShouldBe(2);

        time.Advance(TimeSpan.FromSeconds(1));
        await Eventually.TrueAsync(() => reporter.Batches.Count == 1, "the third attempt succeeds after 2 s more");

        reporter.ReportCalls.ShouldBe(3);
        (await dispatcher.CompleteAsync()).Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task ATransientFailure_WaitsAtLeastTheRetryAfterOfTheSystem()
    {
        var reporter = new FakeReporter { ReportFailure = call => call == 1 ? new TransientReporterException("Throttled.", retryAfter: TimeSpan.FromSeconds(30)) : null };
        await using var dispatcher = Dispatcher([reporter], options => options.BatchSize = 1);
        await dispatcher.StartAsync(TestData.Run(), []);

        dispatcher.Report(TestData.Report("Test"));
        await Eventually.TrueAsync(() => reporter.ReportCalls == 1, "the first attempt runs");
        await Eventually.SettleAsync();
        time.Advance(TimeSpan.FromSeconds(29));
        await Eventually.SettleAsync();
        reporter.ReportCalls.ShouldBe(1);

        time.Advance(TimeSpan.FromSeconds(1));
        await Eventually.TrueAsync(() => reporter.Batches.Count == 1, "the retry runs after 30 s");
    }

    [Fact]
    public async Task ATransientFailure_GivesUpAfterMaxAttempts()
    {
        var reporter = new FakeReporter { ReportFailure = _ => new HttpRequestException("Service Unavailable", null, HttpStatusCode.ServiceUnavailable) };
        await using var dispatcher = Dispatcher([reporter], options =>
        {
            options.BatchSize = 1;
            options.MaxAttempts = 3;
        });
        await dispatcher.StartAsync(TestData.Run(), []);

        dispatcher.Report(TestData.Report("Test"));
        await AdvanceUntilAsync(() => reporter.ReportCalls == 3, TimeSpan.FromSeconds(1), "three attempts ran");
        var summary = await dispatcher.CompleteAsync();

        summary.Warnings.ShouldBe(["Fake: sending 1 result(s) failed after 3 attempts: HttpRequestException: Service Unavailable"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public void IsTransient_TakesThrottlingAndServerErrors(HttpStatusCode status, bool transient)
    {
        TransientFailures.IsTransient(new HttpRequestException("failed", null, status), CancellationToken.None).ShouldBe(transient);
    }

    [Fact]
    public void IsTransient_TakesNetworkFailuresTimeoutsAndTheMarker_ButNotARequestedCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        TransientFailures.IsTransient(new HttpRequestException("No such host."), CancellationToken.None).ShouldBeTrue();
        TransientFailures.IsTransient(new TimeoutException(), CancellationToken.None).ShouldBeTrue();
        TransientFailures.IsTransient(new TaskCanceledException("HttpClient.Timeout"), CancellationToken.None).ShouldBeTrue();
        TransientFailures.IsTransient(new TransientReporterException("busy"), CancellationToken.None).ShouldBeTrue();
        TransientFailures.IsTransient(new TaskCanceledException(), cancelled.Token).ShouldBeFalse();
        TransientFailures.IsTransient(new InvalidOperationException(), CancellationToken.None).ShouldBeFalse();
    }

    [Fact]
    public async Task CompleteAsync_StopsWaitingForAStuckReporter_AtTheDeadline()
    {
        var reporter = new FakeReporter("Testiny") { ReportGate = new TaskCompletionSource() };
        var resultsFile = MockUnixSupport.Path(@"C:\run\results.jsonl");
        await using var dispatcher = Dispatcher([reporter], options =>
        {
            options.BatchSize = 1;
            options.CompleteTimeout = TimeSpan.FromSeconds(10);
            options.FileSystem = new MockFileSystem();
            options.ResultsFilePath = resultsFile;
        });
        await dispatcher.StartAsync(TestData.Run(), []);

        dispatcher.Report(TestData.Report("Test"));
        await Eventually.TrueAsync(() => reporter.ReportCalls == 1, "the reporter is stuck in ReportAsync");

        var completion = dispatcher.CompleteAsync();
        await Eventually.SettleAsync();
        completion.IsCompleted.ShouldBeFalse();
        time.Advance(TimeSpan.FromSeconds(10));

        var summary = await completion.WaitAsync(TimeSpan.FromSeconds(5));
        summary.Warnings.ShouldContain($"Testiny: 1 result(s) were not sent within 10 s; send them again with Reporting:ReplayFile={resultsFile}.");
        reporter.CompletedSummary.ShouldNotBeNull();
        reporter.Disposed.ShouldBeTrue();
        reporter.ReportGate.SetResult();
    }

    [Fact]
    public async Task CompleteAsync_ACancelledCaller_StopsOnlyItsOwnWait_AndTheDrainGoesOn()
    {
        var reporter = new FakeReporter("Testiny") { ReportGate = new TaskCompletionSource() };
        await using var dispatcher = Dispatcher([reporter], options => options.BatchSize = 1);
        await dispatcher.StartAsync(TestData.Run(), []);
        dispatcher.Report(TestData.Report("Test"));
        await Eventually.TrueAsync(() => reporter.ReportCalls == 1, "the reporter is in ReportAsync");

        using var cancelled = new CancellationTokenSource();
        var first = dispatcher.CompleteAsync(cancellationToken: cancelled.Token);
        await cancelled.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => first);

        reporter.ReportGate.SetResult();
        var summary = await dispatcher.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(5));

        summary.Warnings.ShouldBeEmpty();
        reporter.Results.ShouldHaveSingleItem();
        reporter.CompletedSummary.ShouldNotBeNull();
    }

    [Fact]
    public async Task ResultsFile_RoundTrips_ThroughRead()
    {
        var files = new MockFileSystem();
        var path = MockUnixSupport.Path(@"C:\run\results.jsonl");
        var planned = new[] { TestData.Identity("Passes", new TestCaseLink("1234", null)), TestData.Identity("Fails", new TestCaseLink("TC-55", "Testiny")) };
        var failed = TestData.Report(planned[1], TestOutcome.Failed) with
        {
            Artifacts = [new TestArtifact("trace.zip", MockUnixSupport.Path(@"C:\run\SampleTests\Fails\trace.zip"), "application/zip", ArtifactKind.Trace, 2048)],
        };

        await using (var dispatcher = Dispatcher([], options =>
        {
            options.FileSystem = files;
            options.ResultsFilePath = path;
        }))
        {
            await dispatcher.StartAsync(TestData.Run(), planned);
            dispatcher.Report(TestData.Report(planned[0]));
            dispatcher.Report(failed);
            (await dispatcher.CompleteAsync()).Total.ShouldBe(2);
        }

        var content = ResultsFile.Read(files, path);

        content.Warnings.ShouldBeEmpty();
        Json(content.Run.ShouldNotBeNull()).ShouldBe(Json(TestData.Run()));
        Json(content.PlannedTests).ShouldBe(Json(planned));
        content.Results.Count.ShouldBe(2);
        Json(content.Results[0]).ShouldBe(Json(TestData.Report(planned[0])));
        Json(content.Results[1]).ShouldBe(Json(failed));
        content.Results[1].Failure.ShouldNotBeNull().SourceLine.ShouldBe(42);
        files.File.ReadAllText(path).ShouldContain("\"outcome\":\"Failed\"");
    }

    [Fact]
    public void ResultsFile_Read_SkipsACutLastLine()
    {
        var files = new MockFileSystem();
        var path = MockUnixSupport.Path(@"C:\run\results.jsonl");
        var file = new ResultsFile(files, path);
        file.WriteRun(TestData.Run(), []);
        file.Append(TestData.Report("Test"));
        files.File.AppendAllText(path, "{\"kind\":\"result\",\"result\":{\"test\":");

        var content = ResultsFile.Read(files, path);

        content.Results.ShouldHaveSingleItem();
        content.Warnings.ShouldHaveSingleItem().ShouldStartWith($"{path}, line 3, was skipped:");
    }

    [Fact]
    public async Task ZeroReporters_StillWriteTheResultsFile_AndCount()
    {
        var files = new MockFileSystem();
        var path = MockUnixSupport.Path(@"C:\run\results.jsonl");
        await using var dispatcher = Dispatcher([], options =>
        {
            options.FileSystem = files;
            options.ResultsFilePath = path;
        });
        await dispatcher.StartAsync(TestData.Run(), []);

        dispatcher.Report(TestData.Report("Passes"));
        dispatcher.Report(TestData.Report("Fails", TestOutcome.Failed));
        dispatcher.Report(TestData.Report("TimesOut", TestOutcome.Timeout));
        dispatcher.Report(TestData.Report("Skips", TestOutcome.Skipped));
        dispatcher.Report(TestData.Report("Blocks", TestOutcome.Blocked));
        dispatcher.Report(TestData.Report("Flaky", TestOutcome.Failed));
        dispatcher.Report(TestData.Report("Flaky", TestOutcome.Passed));
        var summary = await dispatcher.CompleteAsync(["a settings warning"]);

        (summary.Passed, summary.Failed, summary.Skipped, summary.Blocked).ShouldBe((2, 2, 1, 1));
        summary.Warnings.ShouldBe(["a settings warning"]);
        files.File.ReadAllLines(path).Length.ShouldBe(8);
    }

    [Fact]
    public async Task UnmappedTests_AreWarnedPerSystem_AndFailTheSessionForFail()
    {
        var reporter = new FakeReporter("Testiny");
        await using var dispatcher = Dispatcher([reporter], options => options.UnmappedTests = UnmappedTestsPolicy.Fail);
        var mapped = TestData.Identity("Mapped", new TestCaseLink("1", null));
        var otherSystem = TestData.Identity("OtherSystem", new TestCaseLink("C2", "TestRail"));
        var unmapped = TestData.Identity("Unmapped");
        await dispatcher.StartAsync(TestData.Run(), [mapped, otherSystem, unmapped]);

        var summary = await dispatcher.CompleteAsync();

        summary.Warnings.ShouldBe(["Testiny: 2 test(s) have no [TestCase]: SampleTests.OtherSystem, SampleTests.Unmapped."]);
        dispatcher.UnmappedTests.Select(test => test.MethodName).ShouldBe(["OtherSystem", "Unmapped"]);
        dispatcher.FailsSession.ShouldBeTrue();
    }

    [Fact]
    public async Task UnmappedTests_AreIgnoredByDefault()
    {
        await using var dispatcher = Dispatcher([new FakeReporter()]);
        await dispatcher.StartAsync(TestData.Run(), [TestData.Identity("Unmapped")]);

        (await dispatcher.CompleteAsync()).Warnings.ShouldBeEmpty();
        dispatcher.FailsSession.ShouldBeFalse();
    }

    [Fact]
    public async Task AReporter_GetsOnlyTheCasesOfItsSystem()
    {
        var reporter = new FakeReporter("Testiny");
        await using var dispatcher = Dispatcher([reporter], options => options.BatchSize = 1);
        var test = TestData.Identity("Linked", new TestCaseLink("1", null), new TestCaseLink("2", "testiny"), new TestCaseLink("C3", "TestRail"));
        await dispatcher.StartAsync(TestData.Run(), [test]);

        dispatcher.Report(TestData.Report(test));
        await dispatcher.CompleteAsync();

        reporter.PlannedTests.ShouldHaveSingleItem().Cases.Select(link => link.Id).ShouldBe(["1", "2"]);
        reporter.Results.ShouldHaveSingleItem().Test.Cases.Select(link => link.Id).ShouldBe(["1", "2"]);
        reporter.StartedRun.ShouldNotBeNull().RunId.ShouldBe(TestData.Run().RunId);
        dispatcher.RunUrls["Testiny"].ShouldBe(new Uri("https://reports.example.test/runs/20261031-142500-abcd"));
    }

    [Fact]
    public async Task Report_AfterCompletion_IsAWarning()
    {
        var dispatcher = Dispatcher([new FakeReporter()]);
        await dispatcher.StartAsync(TestData.Run(), []);
        await dispatcher.CompleteAsync();

        dispatcher.Report(TestData.Report("Late"));

        dispatcher.Warnings.ShouldHaveSingleItem().ShouldBe("Fake: a result arrived after the run was completed and was not sent (Late).");
    }

    private static string Json<T>(T value)
    {
        return JsonSerializer.Serialize(value, value!.GetType());
    }

    private ReportingDispatcher Dispatcher(IEnumerable<ITestRunReporter> reporters, Action<ReportingDispatcherOptions>? configure = null)
    {
        var options = new ReportingDispatcherOptions
        {
            FlushInterval = TimeSpan.FromHours(1),
            TimeProvider = time,
        };

        configure?.Invoke(options);
        return new ReportingDispatcher(reporters, options);
    }

    private async Task AdvanceUntilAsync(Func<bool> condition, TimeSpan step, string because)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(5);
            time.Advance(step);
        }

        await Eventually.TrueAsync(condition, because);
    }
}
