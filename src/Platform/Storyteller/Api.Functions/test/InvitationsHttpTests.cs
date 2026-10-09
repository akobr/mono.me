using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class InvitationsHttpTests
{
    [Theory]
    [InlineData("true", IdentityProviderKind.AuthKit, true, true)]
    [InlineData("True", IdentityProviderKind.EntraId, true, true)]
    [InlineData("false", IdentityProviderKind.EntraId, false, false)]
    [InlineData("false", IdentityProviderKind.AuthKit, false, false)]
    [InlineData(null, IdentityProviderKind.AuthKit, true, false)]
    [InlineData(null, IdentityProviderKind.AuthKit, false, true)]
    [InlineData(null, IdentityProviderKind.EntraId, true, true)]
    public void IsEmailVerified_FollowsTheClaimThenTheProvider(string? claim, IdentityProviderKind provider, bool requireVerified, bool expected)
    {
        var claims = new List<Claim> { new("sub", "user_01") };

        if (claim is not null)
        {
            claims.Add(new Claim("email_verified", claim));
        }

        var options = new UserAuthenticationOptions
        {
            Provider = provider,
            AuthKit = new AuthKitOptions { RequireVerifiedEmail = requireVerified },
        };

        InvitationsHttp.IsEmailVerified(Request(claims), options).ShouldBe(expected);
    }

    [Fact]
    public async Task Accept_PassesTheTokenIdentityToTheService()
    {
        var service = new RecordingInvitations();
        var http = CreateHttp(service);
        var request = Request(
        [
            new("sub", "user_01"),
            new("preferred_username", "Ada@Example.com"),
            new("name", "Ada Lovelace"),
            new("email_verified", "true"),
        ]);

        var result = await http.AcceptInvitation(request, "inv-1");

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<Account>().Id.ShouldBe("user_01");
        service.LastInvitee.ShouldBe(new InvitationIdentity("user_01", "Ada@Example.com", true, "Ada@Example.com", "Ada Lovelace"));
        service.Calls.ShouldBe(["accept:inv-1"]);
    }

    [Fact]
    public async Task Accept_TokenWithoutNames_StillReachesTheService()
    {
        var service = new RecordingInvitations();
        var http = CreateHttp(service);

        await http.AcceptInvitation(Request([new("sub", "user_01"), new("email", "ada@example.com")]), "inv-1");

        service.LastInvitee!.Email.ShouldBe("ada@example.com");
        service.LastInvitee.Name.ShouldBeNull();
    }

    [Fact]
    public async Task Mine_WithoutEmailClaim_ReturnsEmptyWithoutQuerying()
    {
        var service = new RecordingInvitations();
        var http = CreateHttp(service);

        var result = await http.GetMyInvitations(Request([new("sub", "user_01")]));

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeAssignableTo<IReadOnlyList<Invitation>>()!.ShouldBeEmpty();
        service.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Create_ServiceRejectsInput_ReturnsBadRequest()
    {
        var service = new RecordingInvitations { CreateError = new ArgumentException("'x' is not a valid email address.") };
        var http = CreateHttp(service);

        var result = await http.CreateInvitation(Request([new("sub", "user_01")]), new InvitationCreate { Email = "x", Role = AccountRole.Reader }, " ACME.Billing ");

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().Message.ShouldContain("valid email");
        service.Calls.ShouldBe(["create:acme.billing:user_01"]);
    }

    [Fact]
    public async Task Create_MissingBody_ReturnsBadRequest()
    {
        var service = new RecordingInvitations();
        var http = CreateHttp(service);

        var result = await http.CreateInvitation(Request([new("sub", "user_01")]), null, "acme.billing");

        result.ShouldBeOfType<BadRequestObjectResult>();
        service.Calls.ShouldBeEmpty();
    }

    private static InvitationsHttp CreateHttp(RecordingInvitations service)
    {
        return new InvitationsHttp(service, Options.Create(new UserAuthenticationOptions()));
    }

    private static TestHttpRequestData Request(List<Claim> claims)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var (context, request) = FunctionTestDoubles.CreateRequest(services);
        claims.Add(new Claim("scp", Scopes.User.Impersonation));
        context.Items[FunctionContextItemKeys.CachedClaims] = claims;
        return request;
    }

    private sealed class RecordingInvitations : IInvitationService
    {
        public List<string> Calls { get; } = [];

        public InvitationIdentity? LastInvitee { get; private set; }

        public Exception? CreateError { get; init; }

        public Task<Invitation> CreateInvitationAsync(string accessPointKey, InvitationCreate model, string actorId, string? actorName)
        {
            Calls.Add($"create:{accessPointKey}:{actorId}");
            return CreateError is null
                ? Task.FromResult(Invitation(accessPointKey))
                : Task.FromException<Invitation>(CreateError);
        }

        public Task<IReadOnlyList<Invitation>> GetPendingInvitationsAsync(string email)
        {
            Calls.Add($"mine:{email}");
            return Task.FromResult<IReadOnlyList<Invitation>>([]);
        }

        public Task<Account> AcceptInvitationAsync(string invitationId, InvitationIdentity invitee)
        {
            Calls.Add($"accept:{invitationId}");
            LastInvitee = invitee;
            return Task.FromResult(new Account
            {
                Id = invitee.AccountId,
                UserName = invitee.Email ?? string.Empty,
                Name = invitee.Name ?? string.Empty,
                AccessMap = new Dictionary<string, AccountRole>(),
            });
        }

        public Task<IReadOnlyList<Invitation>> GetInvitationsAsync(string accessPointKey, string actorId) => throw new NotImplementedException();

        public Task<Invitation> ResendInvitationAsync(string accessPointKey, string invitationId, string actorId) => throw new NotImplementedException();

        public Task<Invitation> RevokeInvitationAsync(string accessPointKey, string invitationId, string actorId) => throw new NotImplementedException();

        public Task<Invitation> DeclineInvitationAsync(string invitationId, InvitationIdentity invitee) => throw new NotImplementedException();

        private static Invitation Invitation(string accessPointKey) => new()
        {
            Id = "inv-1",
            AccessPointKey = accessPointKey,
            Email = "ada@example.com",
            Role = AccountRole.Reader,
            Status = InvitationStatus.Pending,
            InvitedById = "user_01",
            CreatedAt = DateTimeOffset.UnixEpoch,
            ExpiresAt = DateTimeOffset.UnixEpoch.AddDays(7),
        };
    }
}
