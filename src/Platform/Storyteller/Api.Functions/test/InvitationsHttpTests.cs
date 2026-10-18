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
    public async Task Mine_WithUnverifiedEmail_ReturnsEmptyWithoutQuerying()
    {
        var service = new RecordingInvitations();
        var http = CreateHttp(service);

        var result = await http.GetMyInvitations(Request(
        [
            new("sub", "user_01"),
            new("email", "ada@example.com"),
            new("email_verified", "false"),
        ]));

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeAssignableTo<IReadOnlyList<Invitation>>()!.ShouldBeEmpty();
        service.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Create_ServiceRejectsInput_ReturnsBadRequest()
    {
        var service = new RecordingInvitations { CreateError = new ArgumentException("'x' is not a valid email address.") };
        var http = CreateHttp(service);
        var body = FunctionTestDoubles.ToJsonBody(new InvitationCreate { Email = "x", Role = AccountRole.Reader });

        var result = await http.CreateInvitation(Request([new("sub", "user_01")], body), " ACME.Billing ");

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().Message.ShouldContain("valid email");
        service.Calls.ShouldBe(["create:acme.billing:user_01"]);
    }

    [Fact]
    public async Task Create_MissingBody_ReturnsBadRequest()
    {
        var service = new RecordingInvitations();
        var http = CreateHttp(service);

        var result = await http.CreateInvitation(Request([new("sub", "user_01")], string.Empty), "acme.billing");

        result.ShouldBeOfType<BadRequestObjectResult>();
        service.Calls.ShouldBeEmpty();
    }

    // The worker's [FromBody] binding failed these bodies with a 500 before the handler ran. The email and the role
    // are required, so {} is not an invitation either.
    [Theory]
    [InlineData("", "The invitation is missing.")]
    [InlineData("null", "The invitation is missing.")]
    [InlineData("[1, 2]", "The request body is not an invitation (at $).")]
    [InlineData("{}", "The request body is not an invitation (at $).")]
    public async Task Create_MissingOrInvalidBody_ReturnsBadRequestWithoutCallingTheService(string body, string message)
    {
        var service = new RecordingInvitations();
        var http = CreateHttp(service);

        var result = await http.CreateInvitation(Request([new("sub", "user_01")], body), "acme.billing");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe(message);
        error.ErrorCode.ShouldBeNull();
        service.Calls.ShouldBeEmpty();
    }

    // The worker's [FromBody] binding refused a body without a content type; the handler reads it.
    [Fact]
    public async Task Create_BodyWithoutAContentType_IsRead()
    {
        var service = new RecordingInvitations();
        var http = CreateHttp(service);
        var body = FunctionTestDoubles.ToJsonBody(new InvitationCreate { Email = "ada@example.com", Role = AccountRole.Reader });

        var result = await http.CreateInvitation(Request([new("sub", "user_01")], body, contentType: false), "acme.billing");

        result.ShouldBeOfType<OkObjectResult>();
        service.Calls.ShouldBe(["create:acme.billing:user_01"]);
        service.LastCreated.ShouldBe(new InvitationCreate { Email = "ada@example.com", Role = AccountRole.Reader });
    }

    [Fact]
    public async Task Create_PropertyAndEnumNamesInAnyCase_AreRead()
    {
        var service = new RecordingInvitations();
        var http = CreateHttp(service);
        const string body = """{ "email": "ada@example.com", "ROLE": "contributor", "expiresInDays": 3 }""";

        var result = await http.CreateInvitation(Request([new("sub", "user_01")], body), "acme.billing");

        result.ShouldBeOfType<OkObjectResult>();
        service.LastCreated.ShouldBe(new InvitationCreate { Email = "ada@example.com", Role = AccountRole.Contributor, ExpiresInDays = 3 });
    }

    private static InvitationsHttp CreateHttp(RecordingInvitations service)
    {
        return new InvitationsHttp(service, Options.Create(new UserAuthenticationOptions()), FunctionTestDoubles.ApiJsonOptions);
    }

    // A request with the claims; a body goes as JSON, with its content type unless contentType is false.
    private static TestHttpRequestData Request(List<Claim> claims, string? body = null, bool contentType = true)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var headers = body is not null && contentType ? FunctionTestDoubles.JsonContentType() : null;
        var (context, request) = FunctionTestDoubles.CreateRequest(services, headers, body: body);
        claims.Add(new Claim("scp", Scopes.User.Impersonation));
        context.Items[FunctionContextItemKeys.CachedClaims] = claims;
        return request;
    }

    private sealed class RecordingInvitations : IInvitationService
    {
        public List<string> Calls { get; } = [];

        public InvitationIdentity? LastInvitee { get; private set; }

        // The model the handler passed to CreateInvitationAsync; null while nothing was created.
        public InvitationCreate? LastCreated { get; private set; }

        public Exception? CreateError { get; init; }

        public Task<Invitation> CreateInvitationAsync(string accessPointKey, InvitationCreate model, string actorId, string? actorName)
        {
            Calls.Add($"create:{accessPointKey}:{actorId}");
            LastCreated = model;
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
