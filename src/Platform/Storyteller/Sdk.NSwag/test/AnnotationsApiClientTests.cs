using System.Net;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.UnitTests;

public class AnnotationsApiClientTests
{
    private const string BaseUrl = "http://localhost:7071/api";

    private const string ExecutionJson = """
        {
          "AnnotationType": "Execution",
          "ProjectName": "billing",
          "ViewName": "default",
          "AnnotationKey": "exe.northwind.invoicing.prod",
          "Name": "prod",
          "ValidFrom": "2026-10-01T00:00:00+02:00",
          "Values": {
            "renewal": "2026-10-20T12:00:00.1230000+02:00",
            "plan": { "since": "2026-01-01T00:00:00Z" }
          },
          "Modified": { "At": "2026-10-19T08:30:00Z", "By": { "Kind": "Account", "Id": "user_1" } },
          "SubjectName": "northwind",
          "ResponsibilityName": "invoicing",
          "ContextName": "prod"
        }
        """;

    private readonly StubHttpHandler _http = new();

    [Fact]
    public async Task DateLikeStrings_InTheValues_StayStrings()
    {
        _http.Respond(HttpStatusCode.OK, ExecutionJson);

        var annotation = await CreateClient().GetAnnotationAsync("acme", "billing", "default", "exe.northwind.invoicing.prod", true, true);

        var execution = annotation.ShouldBeOfType<Execution>();
        execution.Values["renewal"].ShouldBeOfType<string>().ShouldBe("2026-10-20T12:00:00.1230000+02:00");
        var since = execution.Values["plan"].ShouldBeOfType<JObject>()["since"]!;
        since.Type.ShouldBe(JTokenType.String);
        since.Value<string>().ShouldBe("2026-01-01T00:00:00Z");
    }

    [Fact]
    public async Task TypedDates_StillParse()
    {
        _http.Respond(HttpStatusCode.OK, ExecutionJson);

        var annotation = await CreateClient().GetAnnotationAsync("acme", "billing", "default", "exe.northwind.invoicing.prod", true, true);

        annotation.ValidFrom.ShouldBe(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(2)));
        annotation.ValidFrom!.Value.Offset.ShouldBe(TimeSpan.FromHours(2));
        annotation.Modified.At.ShouldBe(new DateTimeOffset(2026, 10, 19, 8, 30, 0, TimeSpan.Zero));
    }

    private AnnotationsApiClient CreateClient()
    {
        return new AnnotationsApiClient(new HttpClient(_http), new SdkConfiguration { BaseUrl = BaseUrl });
    }
}
