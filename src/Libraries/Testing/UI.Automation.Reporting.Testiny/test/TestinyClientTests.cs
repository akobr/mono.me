using System.Net;
using _42.Testing.UI.Automation.Reporting.Testiny.Api;
using _42.Testing.UI.Automation.Reporting.Testiny.UnitTests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace _42.Testing.UI.Automation.Reporting.Testiny.UnitTests;

public sealed class TestinyClientTests
{
    private readonly FakeTestiny testiny = new();
    private readonly FakeTimeProvider time = new(TestData.Start);

    [Fact]
    public async Task CreateTestRunAsync_PostsTheRunWithTheApiKey_AndReadsItsIdAndEtag()
    {
        var created = await Client().CreateTestRunAsync(new TestinyTestRun { Title = "Nightly", ProjectId = 42, Description = "<!--markdown-->\nbody" });

        created.Id.ShouldBe(7);
        created.ETag.ShouldBe("e1");
        var request = testiny.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe("POST");
        request.Path.ShouldBe("testrun");
        request.Headers[TestinyClient.ApiKeyHeader].ShouldBe("secret-key");
        request.ContentType.ShouldBe("application/json");

        // System.Text.Json escapes < and > in strings; it is the same JSON value.
        request.Text.ShouldBe("""{"title":"Nightly","project_id":42,"description":"\u003C!--markdown--\u003E\nbody"}""");
        request.Json.GetProperty("description").GetString().ShouldBe("<!--markdown-->\nbody");
    }

    [Fact]
    public async Task UpdateTestRunAsync_PutsOnlyWhatIsSet_WithTheEtag()
    {
        var client = Client();
        await client.CreateTestRunAsync(new TestinyTestRun { Title = "Nightly", ProjectId = 42 });

        var updated = await client.UpdateTestRunAsync(new TestinyTestRun { Id = 7, IsClosed = true, ETag = "e1" });

        updated.ETag.ShouldBe("e2");
        updated.IsClosed.ShouldBe(true);
        var request = testiny.To("PUT", "testrun/7").ShouldHaveSingleItem();
        request.Text.ShouldBe("""{"id":7,"is_closed":true,"_etag":"e1"}""");
    }

    [Fact]
    public async Task MapTestCasesAsync_PostsTheBulkMapping()
    {
        await Client().MapTestCasesAsync(
            [new TestinyTestCaseRunMapping(new TestinyTestCaseRunIds(1234, 7), new TestinyResultValues("PASSED"))],
            TestinyMappingOperation.AddOrUpdate);

        var request = testiny.Requests.ShouldHaveSingleItem();
        request.Path.ShouldBe("testrun/mapping/bulk/testcase:testrun");
        request.Query["op"].ShouldBe("add_or_update");
        request.Text.ShouldBe("""[{"ids":{"testcase_id":1234,"testrun_id":7},"mapped":{"result_status":"PASSED"}}]""");
    }

    [Fact]
    public async Task UploadResultAttachmentAsync_PostsTheBytes_WithTheMetadataInTheQuery()
    {
        var blob = await Client().UploadResultAttachmentAsync(7, 1234, new MemoryStream([1, 2, 3]), "SampleTests.Fails: failure.png", "failure 1.png", "image/png");

        blob.Id.ShouldBe(500);
        var request = testiny.Requests.ShouldHaveSingleItem();
        request.Path.ShouldBe("testrun/upload-attachment/7/1234");
        request.Query["title"].ShouldBe("SampleTests.Fails: failure.png");
        request.Query["filename"].ShouldBe("failure 1.png");
        request.Query["mime_type"].ShouldBe("image/png");
        request.ContentType.ShouldBe("application/octet-stream");
        request.Body.ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task CommentRoutes_CreateTheCommentsInBulk_AndMapThemToResults()
    {
        var client = Client();

        var created = await client.CreateCommentsAsync([new TestinyComment { ProjectId = 42, Type = TestinyComment.TextType, Text = "a" }, new TestinyComment { ProjectId = 42, Type = "TEXT", Text = "b" }]);
        await client.MapCommentsToResultsAsync([new TestinyCommentRunMapping(new TestinyCommentRunIds(created[0].Id!.Value, 1234, 7))]);

        created.Select(comment => comment.Id).ShouldBe([100L, 101L]);
        testiny.Requests[0].Path.ShouldBe("comment/bulk");
        testiny.Requests[0].Text.ShouldBe("""[{"project_id":42,"type":"TEXT","text":"a"},{"project_id":42,"type":"TEXT","text":"b"}]""");
        testiny.Requests[1].Path.ShouldBe("comment/mapping/bulk/comment:testcase:testrun");
        testiny.Requests[1].Query["op"].ShouldBe("add");
        testiny.Requests[1].Text.ShouldBe("""[{"ids":{"comment_id":100,"testcase_id":1234,"testrun_id":7}}]""");
    }

    [Fact]
    public async Task FindTestCasesAsync_AsksForTheIdsOfTheProject_200PerRequest()
    {
        testiny.KnownCases.UnionWith([1, 250, 399]);

        var found = await Client().FindTestCasesAsync(42, Enumerable.Range(1, 400).Select(id => (long)id));

        found.Order().ShouldBe([1L, 250L, 399L]);
        var requests = testiny.To("POST", "testcase/find");
        requests.Count.ShouldBe(2);
        requests[0].Json.GetProperty("ids").GetArrayLength().ShouldBe(200);
        requests[0].Json.GetProperty("filter").GetProperty("project_id").GetInt64().ShouldBe(42);
        requests[0].Json.GetProperty("idOnly").GetBoolean().ShouldBeTrue();
        requests[0].Json.GetProperty("pagination").GetProperty("limit").GetInt32().ShouldBe(200);
    }

    [Fact]
    public async Task TooManyRequests_IsTransient_WithItsRetryAfter()
    {
        testiny.Fail("POST", "testrun", HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromSeconds(7), body: """{"message":"Rate limit exceeded"}""");

        var exception = await Should.ThrowAsync<TestinyTransientException>(() => Client().CreateTestRunAsync(new TestinyTestRun { Title = "x", ProjectId = 42 }));

        exception.ShouldBeAssignableTo<TransientReporterException>();
        exception.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        exception.RetryAfter.ShouldBe(TimeSpan.FromSeconds(7));
        exception.Message.ShouldBe("Testiny POST /testrun answered 429 Too Many Requests: Rate limit exceeded");
    }

    [Fact]
    public async Task RetryAfterAsADate_IsTheTimeUntilThen_AndCappedAtFiveMinutes()
    {
        testiny.Intercept = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(time.GetUtcNow().AddSeconds(30));
            return Task.FromResult<HttpResponseMessage?>(response);
        };

        var dated = await Should.ThrowAsync<TestinyTransientException>(() => Client().GetTestRunAsync(7));
        testiny.Fail("GET", "testrun", HttpStatusCode.ServiceUnavailable, retryAfter: TimeSpan.FromHours(1));
        testiny.Intercept = null;
        var capped = await Should.ThrowAsync<TestinyTransientException>(() => Client().GetTestRunAsync(7));

        dated.RetryAfter.ShouldBe(TimeSpan.FromSeconds(30));
        capped.RetryAfter.ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task ServerErrorsAndRequestTimeouts_AreTransient_WithoutRetryAfter(HttpStatusCode status)
    {
        testiny.Fail("GET", "testrun", status);

        var exception = await Should.ThrowAsync<TestinyTransientException>(() => Client().GetTestRunAsync(7));

        exception.StatusCode.ShouldBe(status);
        exception.RetryAfter.ShouldBeNull();
    }

    [Fact]
    public async Task OtherClientErrors_CarryTheApiError()
    {
        testiny.Fail("POST", "testrun/mapping", HttpStatusCode.BadRequest, body: """{"code":"API_INVALID_INPUT_DATA","status":400,"message":"Unknown testcase_id 99","reqid":"r-1"}""");

        var exception = await Should.ThrowAsync<TestinyApiException>(() => Client().MapTestCasesAsync(
            [new TestinyTestCaseRunMapping(new TestinyTestCaseRunIds(99, 7), new TestinyResultValues("NOTRUN"))],
            TestinyMappingOperation.AddOrUpdate));

        exception.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        exception.Error.ShouldNotBeNull().Code.ShouldBe("API_INVALID_INPUT_DATA");
        exception.Error.Message.ShouldBe("Unknown testcase_id 99");
        exception.Error.RequestId.ShouldBe("r-1");
        exception.IsDataError.ShouldBeTrue();
        exception.IsUpdateConflict.ShouldBeFalse();
        exception.Message.ShouldBe("Testiny POST /testrun/mapping/bulk/testcase:testrun answered 400 Bad Request: Unknown testcase_id 99 (API_INVALID_INPUT_DATA, request r-1)");
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "")]
    [InlineData(HttpStatusCode.BadRequest, """{"message":"Update conflict: The entity was already modified elsewhere."}""")]
    public async Task AnUpdateConflict_IsRecognized(HttpStatusCode status, string body)
    {
        testiny.Fail("PUT", "testrun/7", status, body: body);

        var exception = await Should.ThrowAsync<TestinyApiException>(() => Client().UpdateTestRunAsync(new TestinyTestRun { Id = 7, ETag = "old" }));

        exception.IsUpdateConflict.ShouldBeTrue();
    }

    [Fact]
    public async Task AnUnauthorizedKey_IsNeitherTransientNorADataError_AndTheMessageHasNoKey()
    {
        testiny.Fail("POST", "testrun", HttpStatusCode.Unauthorized, body: "not json");

        var exception = await Should.ThrowAsync<TestinyApiException>(() => Client().CreateTestRunAsync(new TestinyTestRun { Title = "x" }));

        exception.IsDataError.ShouldBeFalse();
        exception.Error.ShouldBeNull();
        exception.Message.ShouldBe("Testiny POST /testrun answered 401 Unauthorized: not json");
        exception.Message.ShouldNotContain("secret-key");
    }

    [Fact]
    public async Task ARequestThatTakesTooLong_IsTransient()
    {
        var waiting = new TaskCompletionSource();
        testiny.Intercept = async (_, token) =>
        {
            waiting.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return null;
        };

        var call = Client().GetTestRunAsync(7);
        await waiting.Task;
        time.Advance(TimeSpan.FromSeconds(31));
        var exception = await Should.ThrowAsync<TestinyTransientException>(() => call);

        exception.StatusCode.ShouldBeNull();
        exception.Message.ShouldBe("Testiny GET /testrun/7 did not answer within 30 s.");
    }

    [Fact]
    public async Task AnUpload_HasTheTimeOfItsSize_AtTheSlowestExpectedRate()
    {
        var client = Client(new TestinyOptions { BaseUrl = FakeTestiny.BaseUrl, ApiKey = "secret-key", ProjectId = 42, MinUploadBytesPerSecond = 1024 });
        var waiting = new TaskCompletionSource();
        testiny.Intercept = async (_, token) =>
        {
            waiting.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return null;
        };

        var call = client.UploadResultAttachmentAsync(7, 1, new MemoryStream(new byte[100 * 1024]), "trace", "trace.zip", "application/zip");
        await waiting.Task;
        time.Advance(TimeSpan.FromSeconds(31));
        await Task.Delay(50);
        call.IsCompleted.ShouldBeFalse();
        time.Advance(TimeSpan.FromSeconds(70));
        var exception = await Should.ThrowAsync<TestinyTransientException>(() => call);

        exception.Message.ShouldBe("Testiny POST /testrun/upload-attachment/7/1 did not answer within 100 s.");
        client.UploadTimeout(10).ShouldBe(TimeSpan.FromSeconds(30));
        Client().UploadTimeout(TestinyOptions.DefaultMaxAttachmentBytes).ShouldBe(TimeSpan.FromSeconds(100));
    }

    [Fact]
    public async Task ANetworkFailure_IsTransient()
    {
        testiny.Intercept = (_, _) => throw new HttpRequestException("No such host is known.");

        var exception = await Should.ThrowAsync<TestinyTransientException>(() => Client().GetTestRunAsync(7));

        exception.Message.ShouldBe("Testiny GET /testrun/7 failed: No such host is known.");
    }

    [Fact]
    public async Task ACallerCancellation_IsNotTurnedIntoATransientFailure()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => Client().GetTestRunAsync(7, cancellation.Token));
    }

    [Fact]
    public async Task ABaseUrlWithoutATrailingSlash_StillKeepsItsPath()
    {
        var client = Client(new TestinyOptions { BaseUrl = "https://testiny.example.test/api/v1", ApiKey = "secret-key", ProjectId = 42 });

        var unknown = await Should.ThrowAsync<TestinyApiException>(() => client.GetTestRunAsync(8));

        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        client.BaseUri.ToString().ShouldBe("https://testiny.example.test/api/v1/");
        testiny.Requests.ShouldHaveSingleItem().Path.ShouldBe("testrun/8");
    }

    private TestinyClient Client(TestinyOptions? options = null)
    {
        return new TestinyClient(new HttpClient(testiny), options ?? new TestinyOptions { BaseUrl = FakeTestiny.BaseUrl, ApiKey = "secret-key", ProjectId = 42 }, time);
    }
}
