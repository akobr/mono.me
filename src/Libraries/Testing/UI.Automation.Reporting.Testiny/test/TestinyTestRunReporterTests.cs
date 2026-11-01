using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Text.Json;
using _42.Testing.UI.Automation.Reporting.Testiny.Api;
using _42.Testing.UI.Automation.Reporting.Testiny.UnitTests.Fakes;
using Microsoft.Extensions.Time.Testing;
using static _42.Testing.UI.Automation.Reporting.Testiny.UnitTests.Fakes.TestData;

namespace _42.Testing.UI.Automation.Reporting.Testiny.UnitTests;

public sealed class TestinyTestRunReporterTests
{
    private static readonly string ArtifactsDirectory = MockUnixSupport.Path(@"C:\suite\artifacts\SampleTests");

    private readonly FakeTestiny testiny = new();
    private readonly FakeTimeProvider time = new(Start);
    private readonly MockFileSystem files = new();

    [Fact]
    public async Task StartAsync_CreatesTheRun_AndPlansEveryLinkedCaseAsNotRun_In200sPerRequest()
    {
        var planned = Enumerable.Range(1, 450).Select(id => Test($"T{id}", Case($"{1000 + id}"))).Append(Test("Again", Case("1001"))).ToArray();
        await using var reporter = Reporter();

        await reporter.StartAsync(Run(), planned, CancellationToken.None);

        var create = testiny.Requests[0];
        create.Path.ShouldBe("testrun");
        create.Json.GetProperty("title").GetString().ShouldBe("UI automation 2026-10-31 15:25 mock");
        create.Json.GetProperty("project_id").GetInt64().ShouldBe(42);
        var description = create.Json.GetProperty("description").GetString().ShouldNotBeNull();
        description.ShouldStartWith("<!--markdown-->\n| Setting | Value |");
        description.ShouldContain("started 2026-10-31 14:25:00Z");

        var plans = testiny.To("POST", "testrun/mapping/bulk/testcase:testrun");
        plans.Select(plan => plan.Json.GetArrayLength()).ShouldBe([200, 200, 50]);
        plans.ShouldAllBe(plan => plan.Query["op"] == "add_or_update");
        var items = plans.SelectMany(plan => plan.Json.EnumerateArray()).ToArray();
        items.Select(item => item.GetProperty("ids").GetProperty("testcase_id").GetInt64()).ShouldBe(Enumerable.Range(1001, 450).Select(id => (long)id));
        items.ShouldAllBe(item => item.GetProperty("ids").GetProperty("testrun_id").GetInt64() == 7 && item.GetProperty("mapped").GetProperty("result_status").GetString() == "NOTRUN");

        reporter.RunId.ShouldBe(7);
        reporter.RunUrl.ShouldBe(new Uri("https://testiny.example.test/42/testruns/7"));
        testiny.Requests.ShouldAllBe(request => request.Headers[TestinyClient.ApiKeyHeader] == "secret-key");
    }

    [Fact]
    public async Task StartAsync_AddsTheRunToTheTestPlan_WithTheEtagOfTheCreation()
    {
        await using var reporter = Reporter(options => options.TestPlanId = 5);

        await reporter.StartAsync(Run(), [], CancellationToken.None);

        var update = testiny.To("PUT", "testrun/7").ShouldHaveSingleItem();
        update.Text.ShouldBe("""{"id":7,"testplan_id":5,"_etag":"e1"}""");
        testiny.Run(7)["testplan_id"]!.GetValue<long>().ShouldBe(5);
    }

    [Fact]
    public async Task StartAsync_ATestPlanTestinyRefuses_IsAWarning_AndTheRunGoesOnOutsideIt()
    {
        testiny.Fail("PUT", "testrun/7", HttpStatusCode.NotFound, body: """{"message":"Test plan 5 not found"}""");
        await using var reporter = Reporter(options => options.TestPlanId = 5);

        await reporter.StartAsync(Run(), [Test("A", Case("1"))], CancellationToken.None);
        await reporter.StartAsync(Run(), [Test("A", Case("1"))], CancellationToken.None);

        PlannedIds().ShouldBe([1L]);
        testiny.To("PUT", "testrun/7").ShouldHaveSingleItem();
        reporter.Warnings.ShouldHaveSingleItem().ShouldBe(
            "Testiny did not add run 7 to test plan 5 (Reporting:Testiny:TestPlanId); the run is outside the plan: Testiny PUT /testrun/7 answered 404 Not Found: Test plan 5 not found");
    }

    [Fact]
    public async Task StartAsync_ACaseCheckTestinyRefuses_IsAWarning_AndTheIdsAreUsedUnchecked()
    {
        testiny.Fail("POST", "testcase/find", HttpStatusCode.Forbidden, body: """{"message":"Missing permission"}""");
        await using var reporter = Reporter(validateCaseIds: true);

        await reporter.StartAsync(Run(), [Test("A", Case("1")), Test("B", Case("2"))], CancellationToken.None);
        await reporter.StartAsync(Run(), [Test("A", Case("1")), Test("B", Case("2"))], CancellationToken.None);

        testiny.To("POST", "testcase/find").ShouldHaveSingleItem();
        PlannedIds().ShouldBe([1L, 2L]);
        reporter.Warnings.ShouldHaveSingleItem().ShouldBe("Testiny did not check the [TestCase] ids, so they are used unchecked: Testiny POST /testcase/find answered 403 Forbidden: Missing permission");
    }

    [Fact]
    public async Task DisposeAsync_ClosesARunWhoseStartFailed_WithWhyInItsDescription()
    {
        testiny.Fail("POST", "testrun/mapping", HttpStatusCode.Forbidden, body: """{"message":"Missing permission"}""");
        var reporter = Reporter();

        await Should.ThrowAsync<TestinyApiException>(() => reporter.StartAsync(Run(), [Test("A", Case("1"))], CancellationToken.None));
        await reporter.DisposeAsync();

        testiny.Requests.Select(request => request.ToString()).ShouldBe(["POST testrun", "POST testrun/mapping/bulk/testcase:testrun", "GET testrun/7", "PUT testrun/7"]);
        var update = testiny.To("PUT", "testrun/7").ShouldHaveSingleItem();
        update.Json.GetProperty("is_closed").GetBoolean().ShouldBeTrue();
        var description = update.Json.GetProperty("description").GetString().ShouldNotBeNull();
        description.ShouldStartWith("<!--markdown-->\n| Setting | Value |");
        description.ShouldContain("its reporting to Testiny stopped before the end, because its start failed: TestinyApiException: Testiny POST /testrun/mapping/bulk/testcase:testrun answered 403 Forbidden: Missing permission. Test cases in the run: 0.");
        testiny.Run(7)["is_closed"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task DisposeAsync_ClosesNothing_AfterTheRunCompleted_OrWithoutARun()
    {
        var completed = await Started();
        await completed.CompleteAsync(new TestRunSummary(Start, 0, 0, 0, 0, []), CancellationToken.None);
        await completed.DisposeAsync();
        await Reporter().DisposeAsync();

        testiny.To("PUT", "testrun/7").ShouldHaveSingleItem();
        testiny.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task StartAsync_TakesTheLinksOfTestinyOnly_AndTheDigitsOfAPrefixedId()
    {
        var planned = new[]
        {
            Test("A", Case("TC-12"), Case("13", "Testiny"), Case("14", "TestRail")),
            Test("B", Case("not an id")),
        };
        await using var reporter = Reporter();

        await reporter.StartAsync(Run(), planned, CancellationToken.None);

        PlannedIds().ShouldBe([12L, 13L]);
        reporter.Warnings.ShouldHaveSingleItem().ShouldBe("[TestCase(\"not an id\")] of SampleTests.B is not a Testiny test case id (a number such as 1234, or TC-1234); it is ignored.");
    }

    [Fact]
    public async Task StartAsync_ValidatesTheIds_WhenAsked_AndLeavesTheUnknownOnesOut()
    {
        testiny.KnownCases.UnionWith([1, 2]);
        await using var reporter = Reporter(validateCaseIds: true);

        await reporter.StartAsync(Run(), [Test("A", Case("1")), Test("B", Case("2"), Case("3"))], CancellationToken.None);
        await reporter.ReportAsync([Result(Test("B", Case("2"), Case("3")), TestOutcome.Failed, failure: Failure())], CancellationToken.None);

        var find = testiny.To("POST", "testcase/find").ShouldHaveSingleItem();
        find.Json.GetProperty("ids").EnumerateArray().Select(id => id.GetInt64()).ShouldBe([1L, 2L, 3L]);
        find.Json.GetProperty("omitForbidden").GetBoolean().ShouldBeTrue();
        PlannedIds().ShouldBe([1L, 2L]);
        reporter.Warnings.ShouldHaveSingleItem().ShouldBe("1 [TestCase] id(s) are no test cases of project 42, and their results are not reported: 3 (SampleTests.B).");
        testiny.Requests.ShouldNotContain(request => request.Text.Contains("\"testcase_id\":3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_DoesNotValidate_ByDefault()
    {
        await using var reporter = Reporter();

        await reporter.StartAsync(Run(), [Test("A", Case("1"))], CancellationToken.None);

        testiny.To("POST", "testcase/find").ShouldBeEmpty();
    }

    [Fact]
    public async Task StartAsync_RetriedAfterATransientFailure_CreatesOneRun()
    {
        testiny.Fail("POST", "testrun/mapping", HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromSeconds(3));
        await using var reporter = Reporter();
        var planned = new[] { Test("A", Case("1")) };

        var failure = await Should.ThrowAsync<TestinyTransientException>(() => reporter.StartAsync(Run(), planned, CancellationToken.None));
        await reporter.StartAsync(Run(), planned, CancellationToken.None);

        failure.RetryAfter.ShouldBe(TimeSpan.FromSeconds(3));
        testiny.To("POST", "testrun").Count(request => request.Path == "testrun").ShouldBe(1);
        testiny.To("POST", "testrun/mapping").Count.ShouldBe(2);
    }

    [Fact]
    public async Task StartAsync_RetriedAfterACreationWhoseAnswerWasLost_AdoptsTheCreatedRun()
    {
        testiny.Fail("POST", "testrun", HttpStatusCode.BadGateway, when: request => request.Path == "testrun", afterCommit: true);
        await using var reporter = Reporter();
        var planned = new[] { Test("A", Case("1")) };

        await Should.ThrowAsync<TestinyTransientException>(() => reporter.StartAsync(Run(), planned, CancellationToken.None));
        await reporter.StartAsync(Run(), planned, CancellationToken.None);

        testiny.RunIds.ShouldBe([7L]);
        reporter.RunId.ShouldBe(7);
        var find = testiny.To("POST", "testrun/find").ShouldHaveSingleItem();
        find.Json.GetProperty("filter").GetProperty("project_id").GetInt64().ShouldBe(42);
        find.Json.GetProperty("filter").GetProperty("title").GetString().ShouldBe("UI automation 2026-10-31 15:25 mock");
        find.Json.GetProperty("omitLargeValues").GetBoolean().ShouldBeFalse();
        PlannedIds().ShouldBe([1L]);
    }

    [Fact]
    public async Task StartAsync_RetriedAfterAnUncommittedCreation_CreatesTheRun_IgnoringOtherRunsOfTheTitle()
    {
        await using (var earlier = Reporter())
        {
            await earlier.StartAsync(Run() with { RunId = "earlier-run" }, [], CancellationToken.None);
        }

        testiny.Fail("POST", "testrun", HttpStatusCode.GatewayTimeout, when: request => request.Path == "testrun");
        await using var reporter = Reporter();

        await Should.ThrowAsync<TestinyTransientException>(() => reporter.StartAsync(Run(), [], CancellationToken.None));
        await reporter.StartAsync(Run(), [], CancellationToken.None);

        testiny.RunIds.ShouldBe([7L, 8L]);
        reporter.RunId.ShouldBe(8);
        testiny.To("POST", "testrun/find").ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task StartAsync_RetriedAfterACreationRefusedBeforeTheWork_CreatesWithoutALookup(HttpStatusCode status)
    {
        testiny.Fail("POST", "testrun", status, when: request => request.Path == "testrun");
        await using var reporter = Reporter();

        await Should.ThrowAsync<TestinyTransientException>(() => reporter.StartAsync(Run(), [], CancellationToken.None));
        await reporter.StartAsync(Run(), [], CancellationToken.None);

        testiny.RunIds.ShouldBe([7L]);
        testiny.To("POST", "testrun/find").ShouldBeEmpty();
    }

    [Fact]
    public async Task DisposeAsync_ClosesARunWhoseCreationAnswerWasLost_AndThatWasNotTriedAgain()
    {
        testiny.Fail("POST", "testrun", HttpStatusCode.InternalServerError, when: request => request.Path == "testrun", afterCommit: true);
        var reporter = Reporter();

        await Should.ThrowAsync<TestinyTransientException>(() => reporter.StartAsync(Run(), [], CancellationToken.None));
        await reporter.DisposeAsync();

        testiny.Requests.Select(request => request.ToString()).ShouldBe(["POST testrun", "POST testrun/find", "GET testrun/7", "PUT testrun/7"]);
        testiny.Run(7)["is_closed"]!.GetValue<bool>().ShouldBeTrue();
        testiny.Run(7)["description"]!.GetValue<string>().ShouldContain("because its start failed: TestinyTransientException: Testiny POST /testrun answered 500");
    }

    [Fact]
    public async Task ReportAsync_TheWorstStatusOfACaseWins_OverTestsAndBatches()
    {
        var first = Test("First", Case("1"));
        var second = Test("Second", Case("1"), Case("2"));
        var third = Test("Third", Case("1"));
        await using var reporter = await Started(first, second, third);

        await reporter.ReportAsync([Result(first, TestOutcome.Passed)], CancellationToken.None);
        await reporter.ReportAsync([Result(second, TestOutcome.Failed, failure: Failure())], CancellationToken.None);
        await reporter.ReportAsync([Result(third, TestOutcome.Passed)], CancellationToken.None);

        var results = ResultMappings();
        results.Count.ShouldBe(2);
        results[0].ShouldBe([(1L, "PASSED")]);
        results[1].ShouldBe([(1L, "FAILED"), (2L, "FAILED")]);
    }

    [Fact]
    public async Task ReportAsync_RanksFailedOverBlockedOverSkippedOverPassed()
    {
        var tests = new[] { Test("P", Case("1"), Case("2"), Case("3"), Case("4")), Test("S", Case("2"), Case("3"), Case("4")), Test("B", Case("3"), Case("4")), Test("F", Case("4")) };
        await using var reporter = await Started(tests);

        await reporter.ReportAsync(
            [Result(tests[3], TestOutcome.Timeout), Result(tests[2], TestOutcome.Blocked), Result(tests[1], TestOutcome.Skipped), Result(tests[0], TestOutcome.Passed)],
            CancellationToken.None);

        ResultMappings().ShouldHaveSingleItem().ShouldBe([(1L, "PASSED"), (2L, "SKIPPED"), (3L, "BLOCKED"), (4L, "FAILED")]);
    }

    [Fact]
    public async Task ReportAsync_UsesTheConfiguredStatusesOfSkippedAndBlocked()
    {
        var skipped = Test("S", Case("1"));
        var blocked = Test("B", Case("2"));
        await using var reporter = await Started(
            options =>
            {
                options.ResultForSkipped = "BLOCKED";
                options.ResultForBlocked = "RETEST";
            },
            skipped,
            blocked);

        await reporter.ReportAsync([Result(skipped, TestOutcome.Skipped), Result(blocked, TestOutcome.Blocked)], CancellationToken.None);

        ResultMappings().ShouldHaveSingleItem().ShouldBe([(1L, "BLOCKED"), (2L, "RETEST")]);
    }

    [Fact]
    public async Task ReportAsync_CountsTheLastAttemptOfATestOnly()
    {
        var test = Test("Flaky", Case("1"));
        await using var reporter = await Started(test);

        await reporter.ReportAsync([Result(test, TestOutcome.Failed, attempt: 1, failure: Failure()), Result(test, TestOutcome.Passed, attempt: 2)], CancellationToken.None);
        await reporter.ReportAsync([Result(test, TestOutcome.Failed, attempt: 1, failure: Failure())], CancellationToken.None);

        ResultMappings().ShouldHaveSingleItem().ShouldBe([(1L, "PASSED")]);
        testiny.To("POST", "comment/bulk").ShouldBeEmpty();
    }

    [Fact]
    public async Task ReportAsync_UploadsTheArtifactsOfAFailure_ToEachLinkedCase()
    {
        var failing = Test("Fails", Case("1"), Case("2"));
        var passing = Test("Passes", Case("3"));
        await using var reporter = await Started(options => options.AttachTraces = true, failing, passing);

        await reporter.ReportAsync(
            [
                Result(failing, TestOutcome.Failed, failure: Failure(), artifacts: [Artifact("failure.png", 3, ArtifactKind.Screenshot), Artifact("trace.zip", 5, ArtifactKind.Trace)]),
                Result(passing, TestOutcome.Passed, artifacts: [Artifact("console.log", 2, ArtifactKind.ConsoleLog)]),
            ],
            CancellationToken.None);

        var uploads = testiny.To("POST", "testrun/upload-attachment/");
        uploads.Select(upload => upload.Path).ShouldBe(["testrun/upload-attachment/7/1", "testrun/upload-attachment/7/2", "testrun/upload-attachment/7/1", "testrun/upload-attachment/7/2"]);
        uploads[0].Query["title"].ShouldBe("SampleTests.Fails: failure.png");
        uploads[0].Query["filename"].ShouldBe("failure.png");
        uploads[0].Query["mime_type"].ShouldBe("image/png");
        uploads[0].Body.Length.ShouldBe(3);
        uploads[2].Query["mime_type"].ShouldBe("application/zip");
        uploads[2].Body.Length.ShouldBe(5);
    }

    [Theory]
    [InlineData(TestinyContentPolicy.All, 2)]
    [InlineData(TestinyContentPolicy.None, 0)]
    public async Task ReportAsync_UploadsPerTheAttachmentsPolicy(TestinyContentPolicy policy, int uploads)
    {
        var failing = Test("Fails", Case("1"));
        var passing = Test("Passes", Case("2"));
        await using var reporter = await Started(options => options.Attachments = policy, failing, passing);

        await reporter.ReportAsync(
            [
                Result(failing, TestOutcome.Failed, failure: Failure(), artifacts: [Artifact("failure.png", 3, ArtifactKind.Screenshot)]),
                Result(passing, TestOutcome.Passed, artifacts: [Artifact("console.log", 2, ArtifactKind.ConsoleLog, "Passes")]),
            ],
            CancellationToken.None);

        testiny.To("POST", "testrun/upload-attachment/").Count.ShouldBe(uploads);
    }

    [Fact]
    public async Task ReportAsync_ListsAFileOverTheLimit_AndAMissingOne_InsteadOfUploadingThem()
    {
        var failing = Test("Fails", Case("1"));
        await using var reporter = await Started(
            options =>
            {
                options.MaxAttachmentBytes = 4;
                options.AttachTraces = true;
            },
            failing);
        var gone = new TestArtifact("video.webm", files.Path.Combine(ArtifactsDirectory, "Fails", "video.webm"), "video/webm", ArtifactKind.Video, 9);

        await reporter.ReportAsync(
            [Result(failing, TestOutcome.Failed, failure: Failure(), artifacts: [Artifact("failure.png", 3, ArtifactKind.Screenshot), Artifact("trace.zip", 5, ArtifactKind.Trace), gone])],
            CancellationToken.None);

        testiny.To("POST", "testrun/upload-attachment/").ShouldHaveSingleItem().Query["filename"].ShouldBe("failure.png");
        var text = CommentTexts().ShouldHaveSingleItem();
        text.ShouldContain("- **Artifacts:** `failure.png` (attached, 3 B), `trace.zip` (not attached: 5 B is over the limit of 4 B), `video.webm` (not attached: the file is gone)");
    }

    [Fact]
    public async Task ReportAsync_WritesOneMarkdownCommentPerTestAndLinkedCase_WithTheDetails()
    {
        var failing = Test("Fails", Case("1"), Case("2"));
        await using var reporter = await Started(failing);

        await reporter.ReportAsync([Result(failing, TestOutcome.Failed, failure: Failure(), artifacts: [Artifact("failure.png", 3, ArtifactKind.Screenshot)])], CancellationToken.None);

        var create = testiny.To("POST", "comment/bulk").ShouldHaveSingleItem();
        var comments = create.Json.EnumerateArray().ToArray();
        comments.Length.ShouldBe(2);
        comments.ShouldAllBe(comment => comment.GetProperty("project_id").GetInt64() == 42 && comment.GetProperty("type").GetString() == "TEXT");

        var text = comments[0].GetProperty("text").GetString().ShouldNotBeNull();
        text.ShouldBe(
            """
            <!--markdown-->
            **FAILED** `SampleTests.Fails`

            - **Test:** `_42.Suite.Tests.SampleTests.Fails`
            - **Outcome:** Failed
            - **Duration:** 1.50 s
            - **Started:** 2026-10-31 14:26:00Z
            - **Attempt:** 1
            - **Browser:** chromium 142.0, provider Local, device Pixel 7
            - **Seed:** 1234
            - **Remote session:** SampleTests.Fails
            - **Run:** UI automation 2026-10-31 15:25 mock (`20261031-142500-abcd`)
            - **Build:** <https://github.com/akobr/mono.me/actions/runs/1>
            - **Source:** `SampleTests.cs:21`
            - **Artifacts:** `failure.png` (attached, 3 B)

            **Microsoft.Playwright.PlaywrightException:** Timeout 15000ms exceeded.

            ````text
            Microsoft.Playwright.PlaywrightException: Timeout 15000ms exceeded.
               at _42.Suite.Tests.SampleTests.Fails() in /src/Tests/SampleTests.cs:line 21
               at ```odd frame```
            ````
            """.ReplaceLineEndings("\n"));
        comments[1].GetProperty("text").GetString().ShouldBe(text);

        var mapping = testiny.To("POST", "comment/mapping/bulk/comment:testcase:testrun").ShouldHaveSingleItem();
        mapping.Query["op"].ShouldBe("add");
        mapping.Text.ShouldBe("""[{"ids":{"comment_id":100,"testcase_id":1,"testrun_id":7}},{"ids":{"comment_id":101,"testcase_id":2,"testrun_id":7}}]""");
    }

    [Fact]
    public async Task ReportAsync_CommentsPerTheCommentsPolicy_WithTheReasonOfASkip()
    {
        var passing = Test("Passes", Case("1"));
        var skipped = Test("Skipped", Case("2"));
        await using var reporter = await Started(options => options.Comments = TestinyContentPolicy.All, passing, skipped);

        await reporter.ReportAsync(
            [Result(passing, TestOutcome.Passed), Result(skipped, TestOutcome.Skipped, failure: new TestFailure("No BrowserStack today.", null, null, null, null))],
            CancellationToken.None);

        var texts = CommentTexts();
        texts.Count.ShouldBe(2);
        texts[0].ShouldStartWith("<!--markdown-->\n**PASSED** `SampleTests.Passes`");
        texts[1].ShouldEndWith("**Reason:** No BrowserStack today.");
    }

    [Fact]
    public async Task ReportAsync_WritesNoComment_WhenCommentsIsNone()
    {
        var failing = Test("Fails", Case("1"));
        await using var reporter = await Started(options => options.Comments = TestinyContentPolicy.None, failing);

        await reporter.ReportAsync([Result(failing, TestOutcome.Failed, failure: Failure())], CancellationToken.None);

        testiny.To("POST", "comment/").ShouldBeEmpty();
        ResultMappings().ShouldHaveSingleItem().ShouldBe([(1L, "FAILED")]);
    }

    [Fact]
    public async Task ReportAsync_RetriedAfterATransientFailure_RepeatsOnlyWhatIsMissing()
    {
        var failing = Test("Fails", Case("1"));
        await using var reporter = await Started(failing);
        testiny.Fail("POST", "comment/mapping", HttpStatusCode.ServiceUnavailable);
        IReadOnlyList<TestResultReport> batch = [Result(failing, TestOutcome.Failed, failure: Failure(), artifacts: [Artifact("failure.png", 3, ArtifactKind.Screenshot)])];

        await Should.ThrowAsync<TestinyTransientException>(() => reporter.ReportAsync(batch, CancellationToken.None));
        await reporter.ReportAsync(batch, CancellationToken.None);

        ResultMappings().Count.ShouldBe(1);
        testiny.To("POST", "testrun/upload-attachment/").Count.ShouldBe(1);
        testiny.To("POST", "comment/bulk").Count.ShouldBe(1);
        testiny.To("POST", "comment/mapping").Count.ShouldBe(2);
    }

    [Fact]
    public async Task ReportAsync_ACaseTestinyRefuses_CostsOnlyItsOwnResult()
    {
        var test = Test("Fails", Case("1"), Case("99"));
        var other = Test("Other", Case("2"));
        await using var reporter = await Started(options => options.PlanCasesAsNotRun = false, test, other);
        testiny.Fail("POST", "testrun/mapping", HttpStatusCode.BadRequest, times: 10, body: """{"message":"Unknown testcase_id 99"}""", when: request => request.Text.Contains("\"testcase_id\":99", StringComparison.Ordinal));

        await reporter.ReportAsync([Result(test, TestOutcome.Failed, failure: Failure(), artifacts: [Artifact("failure.png", 3, ArtifactKind.Screenshot)]), Result(other, TestOutcome.Passed)], CancellationToken.None);

        var mappings = testiny.To("POST", "testrun/mapping").Select(request => request.Json.GetArrayLength()).ToArray();
        mappings.ShouldBe([3, 1, 1, 1]);
        testiny.To("POST", "testrun/upload-attachment/").ShouldHaveSingleItem().Path.ShouldBe("testrun/upload-attachment/7/1");
        reporter.Warnings.ShouldHaveSingleItem().ShouldStartWith("Testiny refused test case 99 in run 7, and its results are not reported: Testiny POST /testrun/mapping/bulk/testcase:testrun answered 400 Bad Request: Unknown testcase_id 99");
    }

    [Fact]
    public async Task ReportAsync_AnUploadTestinyRefuses_IsAWarning_AndTheCommentSaysSo()
    {
        var failing = Test("Fails", Case("1"));
        await using var reporter = await Started(failing);
        testiny.Fail("POST", "testrun/upload-attachment/", HttpStatusCode.RequestEntityTooLarge);

        await reporter.ReportAsync([Result(failing, TestOutcome.Failed, failure: Failure(), artifacts: [Artifact("failure.png", 3, ArtifactKind.Screenshot)])], CancellationToken.None);

        CommentTexts().ShouldHaveSingleItem().ShouldContain("`failure.png` (the upload failed)");
        reporter.Warnings.ShouldHaveSingleItem().ShouldStartWith("Uploading failure.png of SampleTests.Fails to test case 1 failed: Testiny POST /testrun/upload-attachment/7/1 answered 413");
    }

    [Fact]
    public async Task ReportAsync_AnUploadThatFailsTransientlyTwice_IsGivenUp_AndTheCommentStillGoesOut()
    {
        var failing = Test("Fails", Case("1"));
        await using var reporter = await Started(options => options.AttachTraces = true, failing);
        testiny.Fail("POST", "testrun/upload-attachment/", HttpStatusCode.ServiceUnavailable, times: 2);
        IReadOnlyList<TestResultReport> batch = [Result(failing, TestOutcome.Failed, failure: Failure(), artifacts: [Artifact("trace.zip", 5, ArtifactKind.Trace), Artifact("console.log", 2, ArtifactKind.ConsoleLog)])];

        await Should.ThrowAsync<TestinyTransientException>(() => reporter.ReportAsync(batch, CancellationToken.None));
        await reporter.ReportAsync(batch, CancellationToken.None);

        testiny.To("POST", "testrun/upload-attachment/").Select(upload => upload.Query["filename"]).ShouldBe(["trace.zip", "trace.zip", "console.log"]);
        CommentTexts().ShouldHaveSingleItem().ShouldContain("`trace.zip` (the upload failed)");
        reporter.Warnings.ShouldHaveSingleItem().ShouldStartWith("Uploading trace.zip of SampleTests.Fails to test case 1 failed 2 times: Testiny POST /testrun/upload-attachment/7/1 answered 503");
    }

    [Fact]
    public async Task ReportAsync_LeavesTheTraceOut_WithoutAttachTraces()
    {
        var failing = Test("Fails", Case("1"));
        await using var reporter = await Started(failing);

        await reporter.ReportAsync([Result(failing, TestOutcome.Failed, failure: Failure(), artifacts: [Artifact("failure.png", 3, ArtifactKind.Screenshot), Artifact("trace.zip", 5, ArtifactKind.Trace)])], CancellationToken.None);

        testiny.To("POST", "testrun/upload-attachment/").ShouldHaveSingleItem().Query["filename"].ShouldBe("failure.png");
        CommentTexts().ShouldHaveSingleItem().ShouldContain("`trace.zip` (not attached)");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task ReportAsync_AnUploadThatMayHaveReachedTestiny_IsNotSentAgain(HttpStatusCode status)
    {
        var failing = Test("Fails", Case("1"));
        await using var reporter = await Started(failing);
        testiny.Fail("POST", "testrun/upload-attachment/", status, afterCommit: true);
        IReadOnlyList<TestResultReport> batch = [Result(failing, TestOutcome.Failed, failure: Failure(), artifacts: [Artifact("failure.png", 3, ArtifactKind.Screenshot), Artifact("console.log", 2, ArtifactKind.ConsoleLog)])];

        await reporter.ReportAsync(batch, CancellationToken.None);
        await reporter.ReportAsync(batch, CancellationToken.None);

        testiny.To("POST", "testrun/upload-attachment/").Select(upload => upload.Query["filename"]).ShouldBe(["failure.png", "console.log"]);
        CommentTexts().ShouldHaveSingleItem().ShouldContain("`failure.png` (the upload failed)");
        reporter.Warnings.ShouldHaveSingleItem().ShouldStartWith("Uploading failure.png of SampleTests.Fails to test case 1 failed (it may have reached Testiny, so it is not sent again):");
    }

    [Fact]
    public async Task ReportAsync_CommentsThatMayHaveReachedTestiny_AreNotCreatedAgain()
    {
        var failing = Test("Fails", Case("1"));
        await using var reporter = await Started(failing);
        testiny.Fail("POST", "comment/bulk", HttpStatusCode.InternalServerError, afterCommit: true);
        IReadOnlyList<TestResultReport> batch = [Result(failing, TestOutcome.Failed, failure: Failure())];

        await reporter.ReportAsync(batch, CancellationToken.None);
        await reporter.ReportAsync(batch, CancellationToken.None);

        testiny.To("POST", "comment/bulk").ShouldHaveSingleItem();
        testiny.To("POST", "comment/mapping").ShouldBeEmpty();
        reporter.Warnings.ShouldHaveSingleItem().ShouldStartWith("Creating 1 comment(s) on the results of test case(s) 1 failed, and they may have reached Testiny unmapped, so they are not sent again:");
    }

    [Fact]
    public async Task ReportAsync_CommentsRefusedWith503_AreCreatedOnTheRetry()
    {
        var failing = Test("Fails", Case("1"));
        await using var reporter = await Started(failing);
        testiny.Fail("POST", "comment/bulk", HttpStatusCode.ServiceUnavailable);
        IReadOnlyList<TestResultReport> batch = [Result(failing, TestOutcome.Failed, failure: Failure())];

        await Should.ThrowAsync<TestinyTransientException>(() => reporter.ReportAsync(batch, CancellationToken.None));
        await reporter.ReportAsync(batch, CancellationToken.None);

        testiny.To("POST", "comment/bulk").Count.ShouldBe(2);
        testiny.To("POST", "comment/mapping").ShouldHaveSingleItem();
        reporter.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReportAsync_ALongArtifactName_IsCutBeforeItsExtension()
    {
        var failing = Test("Fails", Case("1"));
        await using var reporter = await Started(options => options.AttachTraces = true, failing);
        var name = $"Configurations-SetDocument-{new string('x', 130)}.trace.zip";

        await reporter.ReportAsync([Result(failing, TestOutcome.Failed, failure: Failure(), artifacts: [Artifact(name, 3, ArtifactKind.Trace)])], CancellationToken.None);

        var fileName = testiny.To("POST", "testrun/upload-attachment/").ShouldHaveSingleItem().Query["filename"];
        fileName.Length.ShouldBe(120);
        fileName.ShouldStartWith("Configurations-SetDocument-xxx");
        fileName.ShouldEndWith("x.zip");
    }

    [Fact]
    public async Task ReportAsync_ACommentTestinyRefuses_CostsOnlyItself()
    {
        var bad = Test("Bad", Case("1"));
        var good = Test("Good", Case("2"));
        await using var reporter = await Started(bad, good);
        testiny.Fail("POST", "comment/bulk", HttpStatusCode.UnprocessableEntity, times: 10, body: """{"message":"text is too long"}""", when: request => request.Text.Contains("SampleTests.Bad", StringComparison.Ordinal));

        await reporter.ReportAsync([Result(bad, TestOutcome.Failed, failure: Failure()), Result(good, TestOutcome.Failed, failure: Failure())], CancellationToken.None);
        await reporter.ReportAsync([Result(bad, TestOutcome.Failed, failure: Failure()), Result(good, TestOutcome.Failed, failure: Failure())], CancellationToken.None);

        testiny.To("POST", "comment/bulk").Select(request => request.Json.GetArrayLength()).ShouldBe([2, 1, 1]);
        var mapping = testiny.To("POST", "comment/mapping").ShouldHaveSingleItem();
        mapping.Json.GetArrayLength().ShouldBe(1);
        mapping.Json[0].GetProperty("ids").GetProperty("testcase_id").GetInt64().ShouldBe(2);
        reporter.Warnings.ShouldHaveSingleItem().ShouldBe("Testiny refused the comment of SampleTests.Bad on the result of test case 1: Testiny POST /comment/bulk answered 422 Unprocessable Entity: text is too long");
    }

    [Fact]
    public async Task ReportAsync_FromSeveralThreads_KeepsTheStateConsistent()
    {
        var tests = Enumerable.Range(1, 40).Select(index => Test($"T{index}", Case($"{(index % 5) + 1}"))).ToArray();
        await using var reporter = await Started(tests);

        await Task.WhenAll(tests.Select((test, index) => Task.Run(() => reporter.ReportAsync(
            [Result(test, index == 17 ? TestOutcome.Failed : TestOutcome.Passed, failure: index == 17 ? Failure() : null)],
            CancellationToken.None))));

        var last = ResultMappings().SelectMany(mapping => mapping).GroupBy(item => item.CaseId).ToDictionary(group => group.Key, group => group.Last().Status);
        last.ShouldBe(new Dictionary<long, string> { [1] = "PASSED", [2] = "PASSED", [3] = "PASSED", [4] = "FAILED", [5] = "PASSED" }, ignoreOrder: true);
        testiny.To("POST", "comment/bulk").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ReportAsync_BeforeTheRunWasCreated_Fails()
    {
        await using var reporter = Reporter();

        await Should.ThrowAsync<InvalidOperationException>(() => reporter.ReportAsync([Result(Test("A", Case("1")), TestOutcome.Passed)], CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_WritesTheTotalsAndWarningsIntoTheDescription_AndClosesTheRun_WithAFreshEtag()
    {
        var failing = Test("Fails", Case("1"));
        var pending = Test("Pending", Case("2"));
        var invalid = Test("Invalid", Case("x-y"));
        await using var reporter = await Started(failing, pending, invalid);
        await reporter.ReportAsync([Result(failing, TestOutcome.Failed, failure: Failure(), artifacts: [Artifact("failure.png", 3, ArtifactKind.Screenshot)])], CancellationToken.None);
        testiny.TouchRun(7);

        await reporter.CompleteAsync(new TestRunSummary(Start.AddMinutes(2), 3, 1, 0, 0, ["Testiny: sending 1 result(s) failed after 5 attempts: 429 | too many"]), CancellationToken.None);

        var requests = testiny.Requests;
        requests[^2].ToString().ShouldBe("GET testrun/7");
        var update = requests[^1];
        update.ToString().ShouldBe("PUT testrun/7");
        update.Json.GetProperty("_etag").GetString().ShouldBe("e2");
        update.Json.GetProperty("is_closed").GetBoolean().ShouldBeTrue();
        update.Json.GetProperty("id").GetInt64().ShouldBe(7);

        var description = update.Json.GetProperty("description").GetString().ShouldNotBeNull();
        description.ShouldStartWith("<!--markdown-->\n| Setting | Value |");
        description.ShouldContain("| Passed | Failed | Skipped | Blocked | Total |\n|---|---|---|---|---|\n| 3 | 1 | 0 | 0 | 4 |");
        description.ShouldContain("Test cases in the run: 2 (1 FAILED, 1 NOTRUN); 1 attachment(s), 1 comment(s).");
        description.ShouldContain("finished 2026-10-31 14:27:00Z (2 min 0.0 s)");
        description.ShouldContain("### Warnings (2)\n\n- Testiny: sending 1 result(s) failed after 5 attempts: 429 \\| too many\n- \\[TestCase(\"x-y\")\\] of SampleTests.Invalid is not a Testiny test case id");
        testiny.Run(7)["is_closed"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task CompleteAsync_OnAnUpdateConflict_FetchesTheEtagOnceMore()
    {
        await using var reporter = await Started();
        var touched = false;
        testiny.Intercept = (request, _) =>
        {
            if (request.Method == "PUT" && !touched)
            {
                touched = true;
                testiny.TouchRun(7);
            }

            return Task.FromResult<HttpResponseMessage?>(null);
        };

        await reporter.CompleteAsync(new TestRunSummary(Start.AddMinutes(2), 1, 0, 0, 0, []), CancellationToken.None);

        testiny.Requests.Select(request => request.ToString()).ShouldBe(["POST testrun", "GET testrun/7", "PUT testrun/7", "GET testrun/7", "PUT testrun/7"]);
        var puts = testiny.To("PUT", "testrun/7");
        puts[0].Json.GetProperty("_etag").GetString().ShouldBe("e1");
        puts[1].Json.GetProperty("_etag").GetString().ShouldBe("e2");
        testiny.Run(7)["is_closed"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task CompleteAsync_ASecondConflict_Surfaces()
    {
        await using var reporter = await Started();
        testiny.Fail("PUT", "testrun/7", HttpStatusCode.Conflict, times: 2, body: """{"message":"Update conflict: The entity was already modified elsewhere."}""");

        var exception = await Should.ThrowAsync<TestinyApiException>(() => reporter.CompleteAsync(new TestRunSummary(Start, 0, 0, 0, 0, []), CancellationToken.None));

        exception.IsUpdateConflict.ShouldBeTrue();
        testiny.To("PUT", "testrun/7").Count.ShouldBe(2);
    }

    [Fact]
    public async Task CompleteAsync_LeavesTheRunOpen_WithoutCloseRunOnComplete_AndRunsOnce()
    {
        await using var reporter = await Started(options => options.CloseRunOnComplete = false);
        var summary = new TestRunSummary(Start, 0, 0, 0, 0, []);

        await reporter.CompleteAsync(summary, CancellationToken.None);
        await reporter.CompleteAsync(summary, CancellationToken.None);

        var update = testiny.To("PUT", "testrun/7").ShouldHaveSingleItem();
        update.Json.TryGetProperty("is_closed", out _).ShouldBeFalse();
        testiny.Run(7)["is_closed"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public async Task CompleteAsync_WithoutARun_DoesNothing()
    {
        await using var reporter = Reporter();

        await reporter.CompleteAsync(new TestRunSummary(Start, 0, 0, 0, 0, []), CancellationToken.None);

        testiny.Requests.ShouldBeEmpty();
    }

    private TestinyTestRunReporter Reporter(Action<TestinyOptions>? configure = null, bool validateCaseIds = false)
    {
        var options = new TestinyOptions { BaseUrl = FakeTestiny.BaseUrl, ApiKey = "secret-key", ProjectId = 42 };
        configure?.Invoke(options);
        return new TestinyTestRunReporter(new TestinyClient(new HttpClient(testiny), options, time), options, validateCaseIds, files);
    }

    private Task<TestinyTestRunReporter> Started(params TestIdentity[] planned)
    {
        return Started(null, planned);
    }

    private async Task<TestinyTestRunReporter> Started(Action<TestinyOptions>? configure, params TestIdentity[] planned)
    {
        var reporter = Reporter(configure);
        await reporter.StartAsync(Run(), planned, CancellationToken.None);
        return reporter;
    }

    private TestArtifact Artifact(string name, int size, ArtifactKind kind, string test = "Fails")
    {
        var path = files.Path.Combine(ArtifactsDirectory, test, name);
        files.AddFile(path, new MockFileData(new byte[size]));
        var mimeType = Path.GetExtension(name) switch
        {
            ".png" => "image/png",
            ".zip" => "application/zip",
            _ => "text/plain",
        };

        return new TestArtifact(name, path, mimeType, kind, size);
    }

    // The NOTRUN planning of the start, in order.
    private long[] PlannedIds()
    {
        return testiny.To("POST", "testrun/mapping")
            .SelectMany(request => request.Json.EnumerateArray())
            .Where(item => item.GetProperty("mapped").GetProperty("result_status").GetString() == "NOTRUN")
            .Select(item => item.GetProperty("ids").GetProperty("testcase_id").GetInt64())
            .ToArray();
    }

    // The result mappings after the start's planning, one list per request.
    private List<List<(long CaseId, string Status)>> ResultMappings()
    {
        return testiny.To("POST", "testrun/mapping")
            .Select(request => request.Json.EnumerateArray().Select(Item).ToList())
            .Where(items => items.Any(item => item.Status != "NOTRUN"))
            .ToList();

        static (long CaseId, string Status) Item(JsonElement item)
        {
            return (item.GetProperty("ids").GetProperty("testcase_id").GetInt64(), item.GetProperty("mapped").GetProperty("result_status").GetString()!);
        }
    }

    private List<string> CommentTexts()
    {
        return testiny.To("POST", "comment/bulk")
            .SelectMany(request => request.Json.EnumerateArray())
            .Select(comment => comment.GetProperty("text").GetString()!)
            .ToList();
    }
}
