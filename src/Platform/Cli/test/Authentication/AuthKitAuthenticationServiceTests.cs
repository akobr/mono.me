using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Authentication;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Authentication;

public class AuthKitAuthenticationServiceTests
{
    private const string DeviceAuthorization = """
        {
          "device_code": "device-123",
          "user_code": "RRGQ-BJVS",
          "verification_uri": "https://example.authkit.app/device",
          "verification_uri_complete": "https://example.authkit.app/device?user_code=RRGQ-BJVS",
          "expires_in": 300,
          "interval": 5
        }
        """;

    private static readonly DateTimeOffset Start = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly ScriptedHttpHandler _http = new();
    private readonly InMemoryTokenStore _store = new();
    private readonly ManualTimeProvider _time = new(Start);
    private readonly List<TimeSpan> _delays = [];

    [Fact]
    public async Task Login_PendingThenSlowDownThenSuccess_StoresTheSessionAndHonoursTheInterval()
    {
        var accessToken = TestTokens.AccessToken(Start.AddMinutes(5));
        _http.Respond(HttpStatusCode.OK, DeviceAuthorization)
            .Respond(HttpStatusCode.BadRequest, """{"error":"authorization_pending","error_description":"Pending"}""")
            .Respond(HttpStatusCode.BadRequest, """{"error":"slow_down"}""")
            .Respond(HttpStatusCode.OK, TestTokens.SessionJson(accessToken, "refresh-1"));
        DeviceCodePrompt? prompt = null;

        var user = await CreateService().LoginWithDeviceCodeAsync(shown =>
        {
            prompt = shown;
            return Task.CompletedTask;
        });

        prompt.ShouldNotBeNull();
        prompt.UserCode.ShouldBe("RRGQ-BJVS");
        prompt.VerificationUri.ShouldBe(new Uri("https://example.authkit.app/device"));
        prompt.VerificationUriComplete.ShouldBe(new Uri("https://example.authkit.app/device?user_code=RRGQ-BJVS"));
        prompt.ExpiresIn.ShouldBe(TimeSpan.FromMinutes(5));
        _delays.ShouldBe([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6)]);

        _http.Requests[0].Uri.ShouldBe(new Uri("https://api.workos.com/user_management/authorize/device"));
        _http.Requests[0].Form.ShouldBe(new Dictionary<string, string> { ["client_id"] = "client_123" });
        foreach (var poll in _http.Requests.Skip(1))
        {
            poll.Uri.ShouldBe(new Uri("https://api.workos.com/user_management/authenticate"));
            poll.Form["grant_type"].ShouldBe("urn:ietf:params:oauth:grant-type:device_code");
            poll.Form["device_code"].ShouldBe("device-123");
            poll.Form["client_id"].ShouldBe("client_123");
            poll.Authorization.ShouldBeNull();
        }

        user.ShouldBe(new SignedInUser("user_01", "ada@example.com", "Ada Lovelace"));
        _store.Session.ShouldNotBeNull();
        _store.Session.AccessToken.ShouldBe(accessToken);
        _store.Session.RefreshToken.ShouldBe("refresh-1");
        _store.Session.AccessTokenExpiresAt.ShouldBe(Start.AddMinutes(5));
        _store.Session.OrganizationId.ShouldBe("org_01");
        _store.Locks.ShouldBe(1);
    }

    [Theory]
    [InlineData("access_denied", AuthenticationFailureReason.Denied)]
    [InlineData("expired_token", AuthenticationFailureReason.Expired)]
    [InlineData("invalid_client", AuthenticationFailureReason.ServiceError)]
    public async Task Login_TerminalPollError_StopsWithTheReason(string error, AuthenticationFailureReason reason)
    {
        _http.Respond(HttpStatusCode.OK, DeviceAuthorization)
            .Respond(HttpStatusCode.BadRequest, $$"""{"error":"{{error}}","error_description":"Nope"}""");

        var exception = await Should.ThrowAsync<AuthenticationException>(() => CreateService().LoginWithDeviceCodeAsync(_ => Task.CompletedTask));

        exception.Reason.ShouldBe(reason);
        _http.Requests.Count.ShouldBe(2);
        _store.Session.ShouldBeNull();
    }

    [Fact]
    public async Task Login_CodeLifetimeRunsOut_StopsWithoutAnotherPoll()
    {
        _http.Respond(HttpStatusCode.OK, DeviceAuthorization)
            .Respond(HttpStatusCode.BadRequest, """{"error":"authorization_pending"}""");
        var service = CreateService(delay: (interval, _) =>
        {
            _time.Now += TimeSpan.FromMinutes(4);
            return Task.CompletedTask;
        });

        var exception = await Should.ThrowAsync<AuthenticationException>(() => service.LoginWithDeviceCodeAsync(_ => Task.CompletedTask));

        exception.Reason.ShouldBe(AuthenticationFailureReason.Expired);
        _http.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Login_CancelledWhileWaiting_IsReportedAsCancelled()
    {
        _http.Respond(HttpStatusCode.OK, DeviceAuthorization);
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(delay: async (_, token) =>
        {
            await cancellation.CancelAsync();
            token.ThrowIfCancellationRequested();
        });

        var exception = await Should.ThrowAsync<AuthenticationException>(
            () => service.LoginWithDeviceCodeAsync(_ => Task.CompletedTask, cancellationToken: cancellation.Token));

        exception.Reason.ShouldBe(AuthenticationFailureReason.Cancelled);
    }

    // The first shape is what api.workos.com returned on 2026-10-03 for an unknown client id.
    [Theory]
    [InlineData("""{"error":"invalid_client","error_description":"Unknown client."}""", "Unknown client.")]
    [InlineData("""{"code":"invalid_client","message":"CLI Auth is not enabled."}""", "CLI Auth is not enabled.")]
    public async Task Login_AuthorizationRejected_ReportsTheWorkOsMessage(string body, string message)
    {
        _http.Respond(HttpStatusCode.BadRequest, body);

        var exception = await Should.ThrowAsync<AuthenticationException>(() => CreateService().LoginWithDeviceCodeAsync(_ => Task.CompletedTask));

        exception.Reason.ShouldBe(AuthenticationFailureReason.ServiceError);
        exception.Message.ShouldContain(message);
        _http.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Login_OrganizationSelection_RetriesWithTheChosenOrganization()
    {
        _http.Respond(HttpStatusCode.OK, DeviceAuthorization)
            .Respond(HttpStatusCode.Forbidden, """
                {
                  "code": "organization_selection_required",
                  "message": "The user must choose an organization.",
                  "pending_authentication_token": "pending-xyz",
                  "organizations": [ { "id": "org_a", "name": "Alpha" }, { "id": "org_b", "name": "Beta" } ]
                }
                """)
            .Respond(HttpStatusCode.OK, TestTokens.SessionJson(TestTokens.AccessToken(Start.AddMinutes(5)), "refresh-1", "org_b"));
        IReadOnlyList<OrganizationChoice>? offered = null;

        await CreateService().LoginWithDeviceCodeAsync(
            _ => Task.CompletedTask,
            organizations =>
            {
                offered = organizations;
                return Task.FromResult("org_b");
            });

        offered.ShouldBe([new OrganizationChoice("org_a", "Alpha"), new OrganizationChoice("org_b", "Beta")]);
        var selection = _http.Requests[2].Form;
        selection["grant_type"].ShouldBe("urn:workos:oauth:grant-type:organization-selection");
        selection["pending_authentication_token"].ShouldBe("pending-xyz");
        selection["organization_id"].ShouldBe("org_b");
        selection["client_id"].ShouldBe("client_123");
        _store.Session!.OrganizationId.ShouldBe("org_b");
    }

    [Fact]
    public async Task Login_OrganizationSelectionWithoutSelector_TakesTheFirst()
    {
        _http.Respond(HttpStatusCode.OK, DeviceAuthorization)
            .Respond(HttpStatusCode.Forbidden, """{"code":"organization_selection_required","pending_authentication_token":"pending-xyz","organizations":[{"id":"org_a","name":"Alpha"},{"id":"org_b","name":"Beta"}]}""")
            .Respond(HttpStatusCode.OK, TestTokens.SessionJson(TestTokens.AccessToken(Start.AddMinutes(5)), "refresh-1", "org_a"));

        await CreateService().LoginWithDeviceCodeAsync(_ => Task.CompletedTask);

        _http.Requests[2].Form["organization_id"].ShouldBe("org_a");
    }

    [Fact]
    public async Task GetAccessToken_NoSession_ReturnsNullWithoutCallingWorkOs()
    {
        (await CreateService().GetAccessTokenAsync()).ShouldBeNull();
        (await CreateService().GetSignedInUserAsync()).ShouldBeNull();
        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAccessToken_FreshToken_IsReturnedFromTheStore()
    {
        _store.Session = Session("access-fresh", "refresh-1", Start.AddMinutes(2));

        var token = await CreateService().GetAccessTokenAsync();

        token.ShouldBe("access-fresh");
        _http.Requests.ShouldBeEmpty();
        _store.Locks.ShouldBe(0);
    }

    [Fact]
    public async Task GetAccessToken_RefreshRotatesAndNeverReusesTheOldRefreshToken()
    {
        var firstToken = TestTokens.AccessToken(Start.AddMinutes(5), "first");
        var secondToken = TestTokens.AccessToken(Start.AddMinutes(15), "second");
        _store.Session = Session("access-old", "refresh-1", Start.AddSeconds(30));
        _http.Respond(HttpStatusCode.OK, TestTokens.SessionJson(firstToken, "refresh-2"))
            .Respond(HttpStatusCode.OK, TestTokens.SessionJson(secondToken, "refresh-3"));
        var service = CreateService();

        // Inside the 60-second margin, so the token is refreshed.
        (await service.GetAccessTokenAsync()).ShouldBe(firstToken);
        (await service.GetAccessTokenAsync()).ShouldBe(firstToken);
        _time.Now = Start.AddMinutes(10);
        (await service.GetAccessTokenAsync()).ShouldBe(secondToken);

        _http.Requests.Select(request => request.Form["refresh_token"]).ShouldBe(["refresh-1", "refresh-2"]);
        _http.Requests.ShouldAllBe(request => request.Form["grant_type"] == "refresh_token" && request.Form["client_id"] == "client_123");
        _store.Session!.RefreshToken.ShouldBe("refresh-3");
        _store.Locks.ShouldBe(2);
    }

    [Fact]
    public async Task GetAccessToken_RefreshKeepsTheOrganizationWhenTheResponseHasNone()
    {
        _store.Session = Session("access-old", "refresh-1", Start.AddSeconds(-1));
        _http.Respond(HttpStatusCode.OK, TestTokens.SessionJson(TestTokens.AccessToken(Start.AddMinutes(5)), "refresh-2", organizationId: null));

        await CreateService().GetAccessTokenAsync();

        _store.Session!.OrganizationId.ShouldBe("org_01");
    }

    [Fact]
    public async Task GetAccessToken_AnotherProcessRefreshedMeanwhile_UsesItsToken()
    {
        var stale = Session("access-old", "refresh-1", Start.AddSeconds(-1));
        var refreshedElsewhere = Session("access-new", "refresh-2", Start.AddMinutes(5));
        _store.ScriptReads(stale, refreshedElsewhere);

        var token = await CreateService().GetAccessTokenAsync();

        token.ShouldBe("access-new");
        _http.Requests.ShouldBeEmpty();
        _store.Writes.ShouldBe(0);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Refresh token already used."}""")]
    [InlineData(HttpStatusCode.Unauthorized, """{"code":"invalid_refresh_token","message":"Expired"}""")]
    public async Task GetAccessToken_RejectedRefresh_ClearsTheStore(HttpStatusCode status, string body)
    {
        _store.Session = Session("access-old", "refresh-1", Start.AddSeconds(-1));
        _http.Respond(status, body);

        var token = await CreateService().GetAccessTokenAsync();

        token.ShouldBeNull();
        _store.Session.ShouldBeNull();
        _store.Clears.ShouldBe(1);
    }

    [Fact]
    public async Task GetAccessToken_WorkOsOutage_KeepsTheSession()
    {
        var session = Session("access-old", "refresh-1", Start.AddSeconds(-1));
        _store.Session = session;
        _http.Respond(HttpStatusCode.ServiceUnavailable, "upstream down");

        var exception = await Should.ThrowAsync<AuthenticationException>(() => CreateService().GetAccessTokenAsync());

        exception.Reason.ShouldBe(AuthenticationFailureReason.ServiceError);
        _store.Session.ShouldBeSameAs(session);
    }

    [Fact]
    public async Task GetSignedInUser_ReturnsTheStoredUser()
    {
        _store.Session = Session("access-fresh", "refresh-1", Start.AddMinutes(2));

        var user = await CreateService().GetSignedInUserAsync();

        user.ShouldBe(new SignedInUser("user_01", "ada@example.com", "Ada Lovelace"));
    }

    [Fact]
    public async Task Logout_ClearsTheStoreUnderTheLock()
    {
        _store.Session = Session("access-fresh", "refresh-1", Start.AddMinutes(2));

        await CreateService().LogoutAsync();

        _store.Session.ShouldBeNull();
        _store.Locks.ShouldBe(1);
    }

    [Fact]
    public void ReadExpiry_ReadsExpFromTheJwtPayload()
    {
        AuthKitAuthenticationService.ReadExpiry(TestTokens.AccessToken(Start.AddMinutes(5))).ShouldBe(Start.AddMinutes(5));
        AuthKitAuthenticationService.ReadExpiry("not-a-jwt").ShouldBeNull();
        AuthKitAuthenticationService.ReadExpiry("a.%%%.c").ShouldBeNull();
    }

    [Fact]
    public async Task Login_TokenWithoutReadableExpiry_FallsBackToFiveMinutes()
    {
        _http.Respond(HttpStatusCode.OK, DeviceAuthorization)
            .Respond(HttpStatusCode.OK, TestTokens.SessionJson("opaque-token", "refresh-1"));

        await CreateService().LoginWithDeviceCodeAsync(_ => Task.CompletedTask);

        _store.Session!.AccessTokenExpiresAt.ShouldBe(Start.AddMinutes(5));
    }

    private static AuthKitSession Session(string accessToken, string refreshToken, DateTimeOffset expiresAt)
    {
        return new AuthKitSession
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresAt = expiresAt,
            User = new AuthKitSessionUser("user_01", "ada@example.com", "Ada", "Lovelace"),
            OrganizationId = "org_01",
        };
    }

    private AuthKitAuthenticationService CreateService(Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        return new AuthKitAuthenticationService(
            new HttpClient(_http),
            _store,
            new ResolvedAuthentication { Provider = AuthenticationProvider.AuthKit, ClientId = "client_123" },
            _time,
            delay ?? ((interval, _) =>
            {
                _delays.Add(interval);
                return Task.CompletedTask;
            }));
    }
}
