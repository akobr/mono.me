using System.Net;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.UnitTests;

// The hand-written annotation calls that keep the ETag (ApiSdk.Annotations.cs) and the JSON Patch builder.
public class AnnotationsVersionedTests
{
    private const string BaseUrl = "http://localhost:7071/api";
    private const string Tag = "\"a1.IjAwMDAwYTFiIg\"";
    private const string ErrorJson = """{ "Message": "Nope.", "ErrorCode": "AnnotationPreconditionFailed" }""";

    private const string SubjectJson = """
        {
          "AnnotationType": "Subject",
          "ProjectName": "billing",
          "ViewName": "default",
          "AnnotationKey": "sbt.northwind",
          "Name": "northwind",
          "Title": "Northwind",
          "Values": { "since": "2026-01-01T00:00:00Z" }
        }
        """;

    private readonly StubHttpHandler _http = new();

    public static TheoryData<string, int, string?> Failures()
    {
        var data = new TheoryData<string, int, string?>();

        foreach (var operation in new[] { "get", "set", "patch" })
        {
            foreach (var status in new[] { 400, 403, 404, 409, 412, 415, 500 })
            {
                data.Add(operation, status, ErrorJson);
            }

            data.Add(operation, 401, null);
            data.Add(operation, 404, null);
            data.Add(operation, 418, """{ "teapot": true }""");
        }

        return data;
    }

    [Fact]
    public async Task GetVersioned_AnswersTheAnnotationAndItsTag()
    {
        _http.Respond(HttpStatusCode.OK, SubjectJson, ("ETag", Tag));

        var response = await CreateClient().GetAnnotationVersionedAsync("acme", "billing", "default", "sbt.northwind", includeInactive: true);

        response.StatusCode.ShouldBe(200);
        response.ETag.ShouldBe(Tag);
        response.Result.ShouldBeOfType<Subject>().Title.ShouldBe("Northwind");
        response.Result.Values["since"].ShouldBeOfType<string>().ShouldBe("2026-01-01T00:00:00Z");
        var request = _http.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Get);
        request.Uri.ToString().ShouldBe($"{BaseUrl}/v1/acme/billing/default/annotations/sbt.northwind?includeInactive=true");
        request.Headers.ContainsKey("If-Match").ShouldBeFalse();
    }

    [Fact]
    public async Task GetVersioned_BuildsTheRouteOfTheGeneratedRead()
    {
        _http.Respond(HttpStatusCode.OK, SubjectJson);
        _http.Respond(HttpStatusCode.OK, SubjectJson);
        var client = CreateClient();

        var response = await client.GetAnnotationVersionedAsync("acme corp", "bill/ing", "de fault%", "sbt.a+b?c#d&e");
        await client.GetAnnotationAsync("acme corp", "bill/ing", "de fault%", "sbt.a+b?c#d&e");

        response.ETag.ShouldBeNull();
        _http.Requests[0].Uri.OriginalString.ShouldBe(_http.Requests[1].Uri.OriginalString);
    }

    [Fact]
    public async Task SetVersioned_SendsTheConditions_AndAnswersTheNewTag()
    {
        _http.Respond(HttpStatusCode.OK, SubjectJson, ("ETag", Tag));
        _http.Respond(HttpStatusCode.OK, SubjectJson);
        var client = CreateClient();
        var body = new Subject { ProjectName = "billing", ViewName = "default", AnnotationKey = "sbt.northwind", Name = "northwind", Title = "Northwind" };

        var response = await client.SetAnnotationVersionedAsync("acme", "billing", "default", "sbt.northwind", body, ifMatch: "W/\"x\", " + Tag, ifNoneMatch: "*");
        await client.SetAnnotationAsync("acme", "billing", "default", "sbt.northwind", body);

        response.ETag.ShouldBe(Tag);
        var (versioned, generated) = (_http.Requests[0], _http.Requests[1]);
        versioned.Method.ShouldBe(HttpMethod.Post);
        versioned.Headers["If-Match"].ShouldBe("W/\"x\", " + Tag);
        versioned.Headers["If-None-Match"].ShouldBe("*");
        versioned.Uri.ShouldBe(generated.Uri);
        versioned.Body.ShouldBe(generated.Body);
        versioned.ContentType.ShouldBe("application/json");
    }

    [Fact]
    public async Task PatchVersioned_SendsAJsonPatchAsJsonPatch()
    {
        _http.Respond(HttpStatusCode.OK, SubjectJson, ("ETag", Tag));
        var patch = new AnnotationJsonPatch().Set(nameof(Annotation.Title), "Northwind").SetValue("plan", "pro").AddLabel("eu");

        var response = await CreateClient().PatchAnnotationVersionedAsync("acme", "billing", "default", "sbt.northwind", patch.ToJson(), ifMatch: Tag);

        response.ETag.ShouldBe(Tag);
        var request = _http.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Patch);
        request.ContentType.ShouldBe("application/json-patch+json");
        request.Headers["If-Match"].ShouldBe(Tag);
        request.Body.ShouldBe("""[{"op":"add","path":"/Title","value":"Northwind"},{"op":"add","path":"/Values/plan","value":"pro"},{"op":"add","path":"/Labels/-","value":"eu"}]""");
        request.Uri.ToString().ShouldBe($"{BaseUrl}/v1/acme/billing/default/annotations/sbt.northwind");
    }

    [Fact]
    public async Task PatchVersioned_SendsAMergePatchWithItsNulls()
    {
        _http.Respond(HttpStatusCode.OK, SubjectJson);

        await CreateClient().PatchAnnotationVersionedAsync("acme", "billing", "default", "sbt.northwind", JObject.Parse("""{ "Title": null, "Values": { "old": null, "since": "2026-01-01T00:00:00Z" } }"""));

        var request = _http.Requests.ShouldHaveSingleItem();
        request.ContentType.ShouldBe("application/merge-patch+json");
        request.Body.ShouldBe("""{"Title":null,"Values":{"old":null,"since":"2026-01-01T00:00:00Z"}}""");
        request.Headers.ContainsKey("If-Match").ShouldBeFalse();
    }

    [Fact]
    public async Task PatchVersioned_OfAnotherToken_IsRefusedBeforeAnyRequest()
    {
        await Should.ThrowAsync<ArgumentException>(() => CreateClient().PatchAnnotationVersionedAsync("acme", "billing", "default", "sbt.northwind", new JValue("x")));

        _http.Requests.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Failures_ThrowWhatTheGeneratedMethodsThrow(string operation, int status, string? body)
    {
        _http.Respond((HttpStatusCode)status, body);
        _http.Respond((HttpStatusCode)status, body);
        var client = CreateClient();
        var subject = new Subject { ProjectName = "billing", ViewName = "default", AnnotationKey = "sbt.northwind", Name = "northwind" };

        var expected = await Should.ThrowAsync<ApiException>(() => operation switch
        {
            "get" => client.GetAnnotationAsync("acme", "billing", "default", "sbt.northwind"),
            "set" => client.SetAnnotationAsync("acme", "billing", "default", "sbt.northwind", subject),
            _ => client.PatchAnnotationAsync("acme", "billing", "default", "sbt.northwind", new AnnotationMergePatch { Title = "x" }),
        });
        var actual = await Should.ThrowAsync<ApiException>(() => operation switch
        {
            "get" => client.GetAnnotationVersionedAsync("acme", "billing", "default", "sbt.northwind"),
            "set" => client.SetAnnotationVersionedAsync("acme", "billing", "default", "sbt.northwind", subject),
            _ => client.PatchAnnotationVersionedAsync("acme", "billing", "default", "sbt.northwind", JObject.Parse("""{"Title":"x"}""")),
        });

        actual.GetType().ShouldBe(expected.GetType());
        actual.StatusCode.ShouldBe(status);
        actual.Message.ShouldBe(expected.Message);
        actual.Response.ShouldBe(expected.Response);

        if (expected is ApiException<ErrorResponse> typed)
        {
            ((ApiException<ErrorResponse>)actual).Result.ErrorCode.ShouldBe(typed.Result.ErrorCode);
        }
    }

    [Fact]
    public async Task Authorization_IsAppliedThroughTheConfiguration()
    {
        _http.Respond(HttpStatusCode.OK, SubjectJson);
        var client = new AnnotationsApiClient(new HttpClient(_http), new SdkConfiguration { BaseUrl = BaseUrl, AccessTokenFactory = () => "ApiKey 2s.key.secret" });

        await client.PatchAnnotationVersionedAsync("acme", "billing", "default", "sbt.northwind", new AnnotationJsonPatch().ToJson());

        _http.Requests.ShouldHaveSingleItem().Authorization.ShouldBe("ApiKey 2s.key.secret");
    }

    [Fact]
    public void JsonPatch_EscapesNames_AndKeepsValuesAsTheyAre()
    {
        var patch = new AnnotationJsonPatch()
            .SetValue("a/b~c", 1)
            .SetValue("since", new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.FromHours(2)))
            .SetValue("nested", JObject.Parse("""{ "at": "2026-01-01T00:00:00Z" }"""))
            .SetValue("none", null)
            .Clear(nameof(Annotation.Description))
            .Set(nameof(Annotation.Labels), new[] { "eu", "us" });

        patch.Count.ShouldBe(6);
        patch.ToString().ShouldBe(
            """[{"op":"add","path":"/Values/a~1b~0c","value":1},{"op":"add","path":"/Values/since","value":"2026-10-24T00:00:00+02:00"},{"op":"add","path":"/Values/nested","value":{"at":"2026-01-01T00:00:00Z"}},{"op":"add","path":"/Values/none","value":null},{"op":"add","path":"/Description","value":null},{"op":"add","path":"/Labels","value":["eu","us"]}]""");
        AnnotationJsonPatch.Escape("~/").ShouldBe("~0~1");
        new AnnotationJsonPatch().IsEmpty.ShouldBeTrue();
    }

    // The operations are copied out: a change of the copy is not a change of the patch.
    [Fact]
    public void JsonPatch_ToJson_IsACopy()
    {
        var patch = new AnnotationJsonPatch().AddLabel("eu");

        patch.ToJson().Add(new JObject());

        patch.Count.ShouldBe(1);
    }

    private AnnotationsApiClient CreateClient()
    {
        return new AnnotationsApiClient(new HttpClient(_http), new SdkConfiguration { BaseUrl = BaseUrl });
    }
}
