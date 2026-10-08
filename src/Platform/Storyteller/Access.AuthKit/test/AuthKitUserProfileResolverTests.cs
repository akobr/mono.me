using System.Net;
using System.Text;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.AuthKit.UnitTests;

public class AuthKitUserProfileResolverTests
{
    [Fact]
    public async Task Resolve_User_ReadsEmailAndNameWithTheManagementKey()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {"object":"user","id":"user_01","email":"ada@example.com","first_name":"Ada","last_name":"Lovelace","email_verified":true}
            """);
        var resolver = CreateResolver(handler, apiKey: "sk_test_123");

        var profile = await resolver.ResolveAsync("user_01");

        profile.ShouldBe(new UserProfile("ada@example.com", "Ada Lovelace"));
        handler.Requests.Count.ShouldBe(1);
        handler.Requests[0].Method.ShouldBe(HttpMethod.Get);
        handler.Requests[0].RequestUri.ShouldBe(new Uri("https://api.workos.com/user_management/users/user_01"));
        handler.Requests[0].Headers.Authorization!.Scheme.ShouldBe("Bearer");
        handler.Requests[0].Headers.Authorization!.Parameter.ShouldBe("sk_test_123");
    }

    [Fact]
    public async Task Resolve_UserWithoutNames_UsesTheEmailAsTheName()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"id":"user_01","email":"ada@example.com","first_name":null,"last_name":" "}""");
        var resolver = CreateResolver(handler, apiKey: "sk_test_123");

        var profile = await resolver.ResolveAsync("user_01");

        profile.ShouldBe(new UserProfile("ada@example.com", "ada@example.com"));
    }

    [Fact]
    public async Task Resolve_UnknownUser_ReturnsNull()
    {
        var handler = new StubHandler(HttpStatusCode.NotFound, """{"message":"Not found"}""");
        var resolver = CreateResolver(handler, apiKey: "sk_test_123");

        var profile = await resolver.ResolveAsync("user_01");

        profile.ShouldBeNull();
    }

    [Fact]
    public async Task Resolve_WorkOsFailure_Throws()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError, "{}");
        var resolver = CreateResolver(handler, apiKey: "sk_test_123");

        var exception = await Should.ThrowAsync<HttpRequestException>(() => resolver.ResolveAsync("user_01"));

        exception.Message.ShouldNotContain("sk_test_123");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task Resolve_WithoutManagementKey_ReturnsNullWithoutCallingWorkOs(string? apiKey)
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var resolver = CreateResolver(handler, apiKey);

        var profile = await resolver.ResolveAsync("user_01");

        profile.ShouldBeNull();
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetUser_EscapesTheIdAndUsesTheConfiguredBaseUrl()
    {
        var handler = new StubHandler(HttpStatusCode.NotFound, "{}");
        var client = CreateClient(handler, apiKey: "sk_test_123", apiBaseUrl: "https://workos.example/api/");

        await client.GetUserAsync("user_01/../secrets?x=1");

        handler.Requests[0].RequestUri!.AbsoluteUri
            .ShouldBe("https://workos.example/api/user_management/users/user_01%2F..%2Fsecrets%3Fx%3D1");
    }

    [Fact]
    public async Task GetUser_WithoutManagementKey_Throws()
    {
        var client = CreateClient(new StubHandler(HttpStatusCode.OK, "{}"), apiKey: null);

        client.IsConfigured.ShouldBeFalse();
        await Should.ThrowAsync<InvalidOperationException>(() => client.GetUserAsync("user_01"));
    }

    private static AuthKitUserProfileResolver CreateResolver(StubHandler handler, string? apiKey)
    {
        return new AuthKitUserProfileResolver(CreateClient(handler, apiKey));
    }

    private static WorkOsManagementClient CreateClient(StubHandler handler, string? apiKey, string? apiBaseUrl = null)
    {
        var authKit = new AuthKitOptions { ClientId = "client_123", ApiKey = apiKey };
        authKit.ApiBaseUrl = apiBaseUrl ?? authKit.ApiBaseUrl;

        return new WorkOsManagementClient(
            new HttpClient(handler),
            Options.Create(new UserAuthenticationOptions { Provider = IdentityProviderKind.AuthKit, AuthKit = authKit }));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
