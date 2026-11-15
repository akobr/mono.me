using System.Net;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.UnitTests;

// The hand-written project settings calls (ApiSdk.ProjectSettings.cs): the conditional read, and the writes that keep the
// ETag and the nulls of a merge patch.
public class ProjectSettingsTwinsTests
{
    private const string BaseUrl = "http://localhost:7071/api";
    private const string Tag = "\"7\"";
    private const string ErrorJson = """{ "Message": "Nope.", "ErrorCode": "SettingsPreconditionFailed", "Details": { "Version": 7 } }""";

    private const string SettingsJson = """
        {
          "Types": { "sbt": { "Label": "Company", "Plural": "Companies", "Color": "pink", "Icon": "i-lucide-building-2" } },
          "DefaultTimeZone": "Europe/Prague",
          "DateFormat": "dd.mm.yyyy",
          "Version": 7,
          "Modified": { "By": { "Kind": "Account", "Id": "ada", "Name": "Ada" }, "At": "2026-11-15T08:00:00+00:00" }
        }
        """;

    private readonly StubHttpHandler _http = new();

    public static TheoryData<string, int, string?> Failures()
    {
        var data = new TheoryData<string, int, string?>();

        foreach (var operation in new[] { "get", "set", "patch" })
        {
            foreach (var status in new[] { 400, 403, 404, 409, 412, 413, 415, 500 })
            {
                data.Add(operation, status, ErrorJson);
            }

            data.Add(operation, 401, null);
            data.Add(operation, 404, null);
            data.Add(operation, 418, """{ "teapot": true }""");
        }

        // a write documents no 304, so it fails there as the generated write does
        data.Add("set", 304, null);
        data.Add("patch", 304, null);

        return data;
    }

    [Fact]
    public async Task GetConditional_AnswersTheSettingsAndTheirTag()
    {
        _http.Respond(HttpStatusCode.OK, SettingsJson, ("ETag", Tag), ("Cache-Control", "private, no-cache"));

        var response = await CreateClient().GetProjectSettingsConditionalAsync("acme", "billing");

        response.StatusCode.ShouldBe(200);
        response.IsNotModified.ShouldBeFalse();
        response.ETag.ShouldBe(Tag);
        response.CacheControl.ShouldBe("private, no-cache");
        var settings = response.Result.ShouldNotBeNull();
        settings.Version.ShouldBe(7);
        settings.DefaultTimeZone.ShouldBe("Europe/Prague");
        settings.DateFormat.ShouldBe("dd.mm.yyyy");
        settings.Types["sbt"].Label.ShouldBe("Company");
        settings.Types["sbt"].Icon.ShouldBe("i-lucide-building-2");
        settings.Modified.ShouldNotBeNull();
        var request = _http.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Get);
        request.Uri.ToString().ShouldBe($"{BaseUrl}/v1/acme/billing/settings");
        request.Headers.ContainsKey("If-None-Match").ShouldBeFalse();
        request.Headers["Accept"].ShouldBe("application/json");
    }

    [Fact]
    public async Task GetConditional_SendsIfNoneMatch_AndAnswersNotModified()
    {
        _http.Respond(HttpStatusCode.NotModified, null, ("ETag", Tag));

        var response = await CreateClient().GetProjectSettingsConditionalAsync("acme", "billing", Tag);

        response.StatusCode.ShouldBe(304);
        response.IsNotModified.ShouldBeTrue();
        response.Result.ShouldBeNull();
        response.ETag.ShouldBe(Tag);
        _http.Requests.ShouldHaveSingleItem().Headers["If-None-Match"].ShouldBe(Tag);
    }

    [Fact]
    public async Task GetConditional_OfAnEmptyTag_SendsNoCondition()
    {
        _http.Respond(HttpStatusCode.OK, """{ "Types": {}, "Version": 0 }""", ("ETag", "\"0\""));

        var response = await CreateClient().GetProjectSettingsConditionalAsync("acme", "billing", string.Empty);

        response.Result.ShouldNotBeNull().Types.ShouldBeEmpty();
        response.Result.DefaultTimeZone.ShouldBeNull();
        response.ETag.ShouldBe("\"0\"");
        _http.Requests.ShouldHaveSingleItem().Headers.ContainsKey("If-None-Match").ShouldBeFalse();
    }

    [Fact]
    public async Task EveryTwin_BuildsTheRouteOfTheGeneratedMethods()
    {
        for (var i = 0; i < 6; i++)
        {
            _http.Respond(HttpStatusCode.OK, SettingsJson);
        }

        var client = CreateClient();
        var body = new ProjectSettings();

        await client.GetProjectSettingsConditionalAsync("acme corp", "bill/ing?x#y");
        await client.GetProjectSettingsAsync("acme corp", "bill/ing?x#y");
        await client.SetProjectSettingsVersionedAsync("acme corp", "bill/ing?x#y", body);
        await client.SetProjectSettingsAsync("acme corp", "bill/ing?x#y", body);
        await client.PatchProjectSettingsVersionedAsync("acme corp", "bill/ing?x#y", new JObject());
        await client.PatchProjectSettingsAsync("acme corp", "bill/ing?x#y", new ProjectSettingsMergePatch());

        var uris = _http.Requests.Select(request => request.Uri.OriginalString).Distinct().ToList();
        uris.ShouldHaveSingleItem().ShouldBe($"{BaseUrl}/v1/acme%20corp/bill%2Fing%3Fx%23y/settings");
    }

    [Fact]
    public async Task SetVersioned_SendsIfMatch_AndTheBodyOfTheGeneratedWrite()
    {
        _http.Respond(HttpStatusCode.OK, SettingsJson, ("ETag", "\"8\""));
        _http.Respond(HttpStatusCode.OK, SettingsJson);
        var client = CreateClient();
        var body = new ProjectSettings
        {
            Types = new Dictionary<string, ProjectTypeAppearance> { ["sbt"] = new() { Label = "Company", Color = "pink" } },
            DateFormat = "dd.mm.yyyy",
        };

        var response = await client.SetProjectSettingsVersionedAsync("acme", "billing", body, ifMatch: Tag);
        await client.SetProjectSettingsAsync("acme", "billing", body, if_Match: Tag);

        response.StatusCode.ShouldBe(200);
        response.ETag.ShouldBe("\"8\"");
        response.Result.Version.ShouldBe(7);
        var (versioned, generated) = (_http.Requests[0], _http.Requests[1]);
        versioned.Method.ShouldBe(HttpMethod.Put);
        versioned.Headers["If-Match"].ShouldBe(Tag);
        versioned.Headers.ContainsKey("If-None-Match").ShouldBeFalse();
        versioned.ContentType.ShouldBe("application/json");
        versioned.ContentType.ShouldBe(generated.ContentType);
        versioned.Body.ShouldBe(generated.Body);
        versioned.Headers["If-Match"].ShouldBe(generated.Headers["If-Match"]);
    }

    [Fact]
    public async Task SetVersioned_WithoutACondition_SendsNone()
    {
        _http.Respond(HttpStatusCode.OK, SettingsJson, ("ETag", Tag));

        await CreateClient().SetProjectSettingsVersionedAsync("acme", "billing", new ProjectSettings());

        _http.Requests.ShouldHaveSingleItem().Headers.ContainsKey("If-Match").ShouldBeFalse();
    }

    [Fact]
    public async Task PatchVersioned_SendsAMergePatchWithItsNulls()
    {
        _http.Respond(HttpStatusCode.OK, SettingsJson, ("ETag", "\"8\""));
        var patch = JObject.Parse("""{ "Types": { "sbt": null, "unt": { "Label": "Job", "Icon": null } }, "DefaultTimeZone": null }""");

        var response = await CreateClient().PatchProjectSettingsVersionedAsync("acme", "billing", patch, ifMatch: "*");

        response.ETag.ShouldBe("\"8\"");
        var request = _http.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Patch);
        request.ContentType.ShouldBe("application/merge-patch+json");
        request.Headers["If-Match"].ShouldBe("*");
        request.Body.ShouldBe("""{"Types":{"sbt":null,"unt":{"Label":"Job","Icon":null}},"DefaultTimeZone":null}""");
        request.Uri.ToString().ShouldBe($"{BaseUrl}/v1/acme/billing/settings");
    }

    // The generated patch has the same media type, but its DTO leaves the nulls out, so it cannot remove anything.
    [Fact]
    public async Task GeneratedPatch_SendsTheSameMediaType_WithoutNulls()
    {
        _http.Respond(HttpStatusCode.OK, SettingsJson);

        await CreateClient().PatchProjectSettingsAsync("acme", "billing", new ProjectSettingsMergePatch
        {
            Types = new Dictionary<string, ProjectTypeAppearanceMergePatch> { ["unt"] = new() { Label = "Job" } },
        });

        var request = _http.Requests.ShouldHaveSingleItem();
        request.ContentType.ShouldBe("application/merge-patch+json");
        request.Body.ShouldBe("""{"Types":{"unt":{"Label":"Job"}}}""");
    }

    [Fact]
    public async Task PatchVersioned_OfAnotherToken_IsRefusedBeforeAnyRequest()
    {
        var client = CreateClient();

        await Should.ThrowAsync<ArgumentException>(() => client.PatchProjectSettingsVersionedAsync("acme", "billing", new JArray()));
        await Should.ThrowAsync<ArgumentException>(() => client.PatchProjectSettingsVersionedAsync("acme", "billing", JValue.CreateNull()));

        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task NullArguments_AreRefusedBeforeAnyRequest()
    {
        var client = CreateClient();

        await Should.ThrowAsync<ArgumentNullException>(() => client.GetProjectSettingsConditionalAsync(null!, "billing"));
        await Should.ThrowAsync<ArgumentNullException>(() => client.GetProjectSettingsConditionalAsync("acme", null!));
        await Should.ThrowAsync<ArgumentNullException>(() => client.SetProjectSettingsVersionedAsync("acme", "billing", null!));
        await Should.ThrowAsync<ArgumentNullException>(() => client.PatchProjectSettingsVersionedAsync("acme", "billing", null!));

        _http.Requests.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Failures_ThrowWhatTheGeneratedMethodsThrow(string operation, int status, string? body)
    {
        _http.Respond((HttpStatusCode)status, body);
        _http.Respond((HttpStatusCode)status, body);
        var client = CreateClient();

        var expected = await Should.ThrowAsync<ApiException>(() => operation switch
        {
            "get" => client.GetProjectSettingsAsync("acme", "billing"),
            "set" => client.SetProjectSettingsAsync("acme", "billing", new ProjectSettings()),
            _ => client.PatchProjectSettingsAsync("acme", "billing", new ProjectSettingsMergePatch { DateFormat = "dd.mm.yyyy" }),
        });
        var actual = await Should.ThrowAsync<ApiException>(() => operation switch
        {
            "get" => client.GetProjectSettingsConditionalAsync("acme", "billing"),
            "set" => client.SetProjectSettingsVersionedAsync("acme", "billing", new ProjectSettings()),
            _ => client.PatchProjectSettingsVersionedAsync("acme", "billing", JObject.Parse("""{"DateFormat":"dd.mm.yyyy"}""")),
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

    // The read is the only one that documents a 304; the generated read throws on it, the twin answers it.
    [Fact]
    public async Task GeneratedRead_ThrowsOnNotModified_WhereTheTwinAnswers()
    {
        _http.Respond(HttpStatusCode.NotModified, null, ("ETag", Tag));
        _http.Respond(HttpStatusCode.NotModified, null, ("ETag", Tag));
        var client = CreateClient();

        var exception = await Should.ThrowAsync<ApiException>(() => client.GetProjectSettingsAsync("acme", "billing", Tag));
        var response = await client.GetProjectSettingsConditionalAsync("acme", "billing", Tag);

        exception.StatusCode.ShouldBe(304);
        response.IsNotModified.ShouldBeTrue();
    }

    [Fact]
    public async Task Authorization_IsAppliedThroughTheConfiguration()
    {
        _http.Respond(HttpStatusCode.OK, SettingsJson);
        _http.Respond(HttpStatusCode.OK, SettingsJson);
        _http.Respond(HttpStatusCode.OK, SettingsJson);
        var client = new AccessApiClient(new HttpClient(_http), new SdkConfiguration { BaseUrl = BaseUrl, AccessTokenFactory = () => "token" });

        await client.GetProjectSettingsConditionalAsync("acme", "billing");
        await client.SetProjectSettingsVersionedAsync("acme", "billing", new ProjectSettings());
        await client.PatchProjectSettingsVersionedAsync("acme", "billing", new JObject());

        _http.Requests.Select(request => request.Authorization).ShouldAllBe(authorization => authorization == "Bearer token");
    }

    private AccessApiClient CreateClient()
    {
        return new AccessApiClient(new HttpClient(_http), new SdkConfiguration { BaseUrl = BaseUrl });
    }
}
