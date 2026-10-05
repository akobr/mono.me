using System.Net;
using System.Text;
using System.Text.Json;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.AuthKit.UnitTests;

public class WorkOsManagementClientTests
{
    private readonly RecordingHandler _handler = new();

    [Fact]
    public async Task CreateM2MApplication_PostsTheConnectApplication()
    {
        _handler.Respond(HttpStatusCode.Created, """
            {"object":"connect_application","id":"conn_app_01","client_id":"client_01","name":"42.sform.o.p.x","application_type":"m2m","organization_id":"org_machines","scopes":["storyteller:annotation-read"]}
            """);

        var application = await CreateClient().CreateM2MApplicationAsync(
            new WorkOsM2MApplicationCreate("42.sform.o.p.x", "organization=o|project=p|scope=DefaultRead", "org_machines", ["storyteller:annotation-read"]));

        application.Id.ShouldBe("conn_app_01");
        application.ClientId.ShouldBe("client_01");
        var request = _handler.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.ShouldBe(new Uri("https://api.workos.com/connect/applications"));
        request.Authorization.ShouldBe("Bearer sk_test_123");

        using var body = JsonDocument.Parse(request.Body!);
        body.RootElement.GetProperty("name").GetString().ShouldBe("42.sform.o.p.x");
        body.RootElement.GetProperty("application_type").GetString().ShouldBe("m2m");
        body.RootElement.GetProperty("is_first_party").GetBoolean().ShouldBeFalse();
        body.RootElement.GetProperty("organization_id").GetString().ShouldBe("org_machines");
        body.RootElement.GetProperty("description").GetString().ShouldBe("organization=o|project=p|scope=DefaultRead");
        body.RootElement.GetProperty("scopes").EnumerateArray().Select(scope => scope.GetString()).ShouldBe(["storyteller:annotation-read"]);
    }

    [Fact]
    public async Task CreateClientSecret_ReturnsThePlaintextSecret()
    {
        _handler.Respond(HttpStatusCode.Created, """
            {"object":"connect_application_secret","id":"secret_01","secret_hint":"abc123","last_used_at":null,"created_at":"2026-01-15T12:00:00.000Z","updated_at":"2026-01-15T12:00:00.000Z","secret":"abc123def456"}
            """);

        var secret = await CreateClient().CreateClientSecretAsync("conn_app_01");

        secret.Id.ShouldBe("secret_01");
        secret.Secret.ShouldBe("abc123def456");
        secret.CreatedAt.ShouldBe(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));
        _handler.Requests.Single().Uri.ShouldBe(new Uri("https://api.workos.com/connect/applications/conn_app_01/client_secrets"));
        _handler.Requests.Single().Method.ShouldBe(HttpMethod.Post);
    }

    [Fact]
    public async Task CreateClientSecret_WithoutTheSecretValue_Throws()
    {
        _handler.Respond(HttpStatusCode.Created, """{"object":"connect_application_secret","id":"secret_01","secret_hint":"abc123"}""");

        await Should.ThrowAsync<WorkOsApiException>(() => CreateClient().CreateClientSecretAsync("conn_app_01"));
    }

    [Theory]
    [InlineData("""[{"id":"secret_01","secret_hint":"a"},{"id":"secret_02","secret_hint":"b"}]""")]
    [InlineData("""{"object":"list","data":[{"id":"secret_01","secret_hint":"a"},{"id":"secret_02","secret_hint":"b"}]}""")]
    public async Task ListClientSecrets_ReadsAnArrayOrAList(string body)
    {
        _handler.Respond(HttpStatusCode.OK, body);

        var secrets = await CreateClient().ListClientSecretsAsync("conn_app_01");

        secrets.Select(secret => secret.Id).ShouldBe(["secret_01", "secret_02"]);
        secrets.ShouldAllBe(secret => secret.Secret == null);
        _handler.Requests.Single().Method.ShouldBe(HttpMethod.Get);
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task Deletes_TreatAMissingResourceAsAlreadyDeleted(HttpStatusCode status, bool expected)
    {
        _handler.Respond(status, string.Empty).Respond(status, string.Empty);
        var client = CreateClient();

        (await client.DeleteClientSecretAsync("secret_01")).ShouldBe(expected);
        (await client.DeleteApplicationAsync("conn_app_01")).ShouldBe(expected);

        _handler.Requests.Select(request => (request.Method, request.Uri.AbsolutePath)).ShouldBe(
            [
                (HttpMethod.Delete, "/connect/client_secrets/secret_01"),
                (HttpMethod.Delete, "/connect/applications/conn_app_01"),
            ]);
    }

    [Fact]
    public async Task Failure_CarriesTheWorkOsMessageButNotTheKey()
    {
        _handler.Respond(HttpStatusCode.UnprocessableEntity, """{"code":"invalid_request","message":"Permission storyteller:missing does not exist."}""");

        var exception = await Should.ThrowAsync<WorkOsApiException>(() => CreateClient().CreateM2MApplicationAsync(
            new WorkOsM2MApplicationCreate("n", "d", "org_machines", ["storyteller:missing"])));

        exception.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        exception.Message.ShouldContain("Permission storyteller:missing does not exist.");
        exception.Message.ShouldNotContain("sk_test_123");
    }

    [Theory]
    [InlineData("POST", 429, null, true)]
    [InlineData("POST", 500, null, false)]
    [InlineData("POST", null, "http", false)]
    [InlineData("GET", 500, null, true)]
    [InlineData("DELETE", 503, null, true)]
    [InlineData("GET", null, "http", true)]
    [InlineData("GET", 400, null, false)]
    [InlineData("DELETE", 404, null, false)]
    public void Retry_RepeatsOnlyWhatIsSafeToRepeat(string method, int? status, string? exception, bool expected)
    {
        using var response = status is null ? null : new HttpResponseMessage((HttpStatusCode)status.Value);

        var retry = AuthKitEntryPoint.ShouldRetry(
            response,
            exception is null ? null : new HttpRequestException("transport"),
            new HttpMethod(method));

        retry.ShouldBe(expected);
    }

    private WorkOsManagementClient CreateClient()
    {
        return new WorkOsManagementClient(
            new HttpClient(_handler),
            Options.Create(new UserAuthenticationOptions { AuthKit = new AuthKitOptions { ClientId = "client_123", ApiKey = "sk_test_123" } }));
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? Body);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

        public List<RecordedRequest> Requests { get; } = [];

        public RecordingHandler Respond(HttpStatusCode status, string body)
        {
            _responses.Enqueue((status, body));
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));
            var (status, responseBody) = _responses.Dequeue();
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
        }
    }
}
