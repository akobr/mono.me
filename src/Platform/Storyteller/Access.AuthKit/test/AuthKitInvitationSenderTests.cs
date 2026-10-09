using System.Net;
using System.Text;
using System.Text.Json;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.AuthKit.UnitTests;

public class AuthKitInvitationSenderTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Send_PostsAnApplicationInvitationWithTheInviterAndValidity()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created, """
            {"object":"invitation","id":"invitation_01","email":"ada@example.com","state":"pending","expires_at":"2026-10-17T12:00:00.000Z","accept_invitation_url":"https://auth.example.com/invite?invitation_token=abc"}
            """);
        var sender = new AuthKitInvitationSender(CreateClient(handler, "sk_test_123"));

        var externalId = await sender.SendAsync(Invitation("user_01ABC", days: 7));

        externalId.ShouldBe("invitation_01");
        var request = handler.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.ShouldBe(new Uri("https://api.workos.com/user_management/invitations"));
        request.Authorization.ShouldBe("Bearer sk_test_123");

        using var body = JsonDocument.Parse(request.Body!);
        body.RootElement.GetProperty("email").GetString().ShouldBe("ada@example.com");
        body.RootElement.GetProperty("expires_in_days").GetInt32().ShouldBe(7);
        body.RootElement.GetProperty("inviter_user_id").GetString().ShouldBe("user_01ABC");
        body.RootElement.TryGetProperty("organization_id", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Send_InviterIsNotAWorkOsUser_OmitsTheInviter()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created, """{"object":"invitation","id":"invitation_02"}""");
        var sender = new AuthKitInvitationSender(CreateClient(handler, "sk_test_123"));

        await sender.SendAsync(Invitation("00000000-entra-object-id", days: 45));

        using var body = JsonDocument.Parse(handler.Requests.Single().Body!);
        body.RootElement.TryGetProperty("inviter_user_id", out _).ShouldBeFalse();
        body.RootElement.GetProperty("expires_in_days").GetInt32().ShouldBe(30);
    }

    [Fact]
    public async Task Send_WithoutManagementKey_SendsNothing()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created, "{}");
        var sender = new AuthKitInvitationSender(CreateClient(handler, apiKey: null));

        var externalId = await sender.SendAsync(Invitation("user_01", days: 7));
        await sender.RevokeAsync("invitation_01");

        externalId.ShouldBeNull();
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Send_WorkOsError_Throws()
    {
        var handler = new RecordingHandler(HttpStatusCode.UnprocessableEntity, """{"message":"Email is invalid."}""");
        var sender = new AuthKitInvitationSender(CreateClient(handler, "sk_test_123"));

        var exception = await Should.ThrowAsync<WorkOsApiException>(() => sender.SendAsync(Invitation("user_01", days: 7)));

        exception.Message.ShouldContain("Email is invalid.");
    }

    [Fact]
    public async Task Revoke_PostsToTheRevokeEndpoint()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"object":"invitation","id":"invitation_01","state":"revoked"}""");
        var sender = new AuthKitInvitationSender(CreateClient(handler, "sk_test_123"));

        await sender.RevokeAsync("invitation_01");

        var request = handler.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.ShouldBe(new Uri("https://api.workos.com/user_management/invitations/invitation_01/revoke"));
    }

    [Fact]
    public async Task Revoke_UnknownInvitation_IsIgnored()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound, """{"message":"Not found"}""");
        var client = CreateClient(handler, "sk_test_123");

        var revoked = await client.RevokeInvitationAsync("invitation_missing");

        revoked.ShouldBeFalse();
    }

    [Fact]
    public void AddAuthKitUserAuthentication_ReplacesTheDefaultSender()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IInvitationSender, NoopInvitationSender>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Provider"] = "AuthKit",
                ["Auth:AuthKit:ClientId"] = "client_123",
            })
            .Build();

        services.AddAuthKitUserAuthentication(configuration);

        services.Where(descriptor => descriptor.ServiceType == typeof(IInvitationSender))
            .ShouldHaveSingleItem()
            .ImplementationType.ShouldBe(typeof(AuthKitInvitationSender));
    }

    private static Invitation Invitation(string inviterId, int days) => new()
    {
        Id = "0192f0aa",
        AccessPointKey = "acme.billing",
        Email = "ada@example.com",
        Role = AccountRole.Contributor,
        Status = InvitationStatus.Pending,
        InvitedById = inviterId,
        CreatedAt = CreatedAt,
        ExpiresAt = CreatedAt.AddDays(days),
    };

    private static WorkOsManagementClient CreateClient(HttpMessageHandler handler, string? apiKey)
    {
        return new WorkOsManagementClient(
            new HttpClient(handler),
            Options.Create(new UserAuthenticationOptions { AuthKit = new AuthKitOptions { ClientId = "client_123", ApiKey = apiKey } }));
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? Body);

    private sealed class RecordingHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
        }
    }
}
