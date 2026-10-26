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
    private const string EntraAccountId = "8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d:3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2d";

    [Theory]
    [InlineData("true", IdentityProviderKind.AuthKit, true, true)]
    [InlineData("True", IdentityProviderKind.EntraId, true, true)]
    [InlineData("false", IdentityProviderKind.EntraId, false, false)]
    [InlineData("false", IdentityProviderKind.AuthKit, false, false)]
    [InlineData(null, IdentityProviderKind.AuthKit, true, false)]
    [InlineData(null, IdentityProviderKind.AuthKit, false, true)]
    [InlineData(null, IdentityProviderKind.EntraId, true, true)]
    [InlineData("true", IdentityProviderKind.Keycloak, true, true)]
    [InlineData("false", IdentityProviderKind.Keycloak, false, false)]
    [InlineData(null, IdentityProviderKind.Keycloak, true, false)]
    [InlineData(null, IdentityProviderKind.Keycloak, false, false)]
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
        var http = CreateHttp(service, IdentityProviderKind.AuthKit);
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
        var http = CreateHttp(service, IdentityProviderKind.AuthKit);

        await http.AcceptInvitation(Request([new("sub", "user_01"), new("email", "ada@example.com")]), "inv-1");

        service.LastInvitee!.Email.ShouldBe("ada@example.com");
        service.LastInvitee.Name.ShouldBeNull();
    }

    // Accepting creates the account when the invitee has none: under Entra ID it is {tid}:{oid}, as at registration.
    [Fact]
    public async Task Accept_EntraId_PassesTheTenantAndObjectIdAsTheAccountId()
    {
        var service = new RecordingInvitations();
        var http = CreateHttp(service);

        var result = await http.AcceptInvitation(Request([new("sub", EntraAccountId), new("email", "ada@example.com")]), "inv-1");

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<Account>().Id.ShouldBe(EntraAccountId);
        service.LastInvitee!.AccountId.ShouldBe(EntraAccountId);
    }

    // Any other id under Entra ID is a programming error, refused before the service is asked.
    [Theory]
    [InlineData("user_01")]
    [InlineData("8A7B6C5D-4E3F-4A1B-8C9D-0E1F2A3B4C5D:3F2C6A8E-6C1D-4A59-9A47-0A2F4F4C1B2D")]
    public async Task AcceptAndDecline_EntraIdWithAnotherId_AreAProgrammingError(string accountId)
    {
        var service = new RecordingInvitations();
        var http = CreateHttp(service);

        await Should.ThrowAsync<InvalidOperationException>(() => http.AcceptInvitation(Request([new("sub", accountId), new("email", "ada@example.com")]), "inv-1"));
        await Should.ThrowAsync<InvalidOperationException>(() => http.DeclineInvitation(Request([new("sub", accountId), new("email", "ada@example.com")]), "inv-1"));

        service.Calls.ShouldBeEmpty();
        service.LastInvitee.ShouldBeNull();
    }

    // Keycloak's email_verified speaks for the email claim; preferred_username is a username the user may have chosen.
    [Fact]
    public async Task Accept_Keycloak_MatchesTheEmailClaimRatherThanTheUserName()
    {
        var service = new RecordingInvitations();
        var http = CreateHttp(service, IdentityProviderKind.Keycloak);

        await http.AcceptInvitation(
            Request(
            [
                new("sub", "5f1c2b3a-9d8e-4f70-8a6b-1c2d3e4f5a6b"),
                new("preferred_username", "victim@example.com"),
                new("email", "ada@example.com"),
                new("email_verified", "true"),
            ]),
            "inv-1");

        service.LastInvitee!.AccountId.ShouldBe("5f1c2b3a-9d8e-4f70-8a6b-1c2d3e4f5a6b");
        service.LastInvitee.Email.ShouldBe("ada@example.com");
        service.LastInvitee.IsEmailVerified.ShouldBeTrue();
    }

    [Fact]
    public async Task Accept_KeycloakWithoutEmailVerified_IsNotVerified()
    {
        var service = new RecordingInvitations();
        var http = CreateHttp(service, IdentityProviderKind.Keycloak);

        await http.AcceptInvitation(Request([new("sub", "kc-user"), new("email", "ada@example.com")]), "inv-1");

        service.LastInvitee!.IsEmailVerified.ShouldBeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mine_Keycloak_QueriesTheVerifiedEmailClaimOnly(bool withEmail)
    {
        var service = new RecordingInvitations();
        var http = CreateHttp(service, IdentityProviderKind.Keycloak);
        var claims = new List<Claim>
        {
            new("sub", "kc-user"),
            new("preferred_username", "victim@example.com"),
            new("email_verified", "true"),
        };

        if (withEmail)
        {
            claims.Add(new Claim("email", "ada@example.com"));
        }

        await http.GetMyInvitations(Request(claims));

        string[] expected = withEmail ? ["mine:ada@example.com"] : [];
        service.Calls.ShouldBe(expected);
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

    // A call through an actor carries the user name stored on the account as preferred_username, for audit, not what the
    // identity provider asserts now: it is never a verified email, also where a direct token without email_verified is
    // (Entra ID, and AuthKit with RequireVerifiedEmail off). Invitations are answered through the provider's own sign-in.
    [Theory]
    [InlineData(IdentityProviderKind.EntraId)]
    [InlineData(IdentityProviderKind.AuthKit)]
    public void IsEmailVerified_ThroughAnActor_IsFalse(IdentityProviderKind provider)
    {
        var options = TrustingOptions(provider);

        InvitationsHttp.IsEmailVerified(Request(DelegatedClaims(provider)), options).ShouldBeTrue();
        InvitationsHttp.IsEmailVerified(Request(DelegatedClaims(provider), delegatedActor: true), options).ShouldBeFalse();
    }

    [Theory]
    [InlineData(IdentityProviderKind.EntraId)]
    [InlineData(IdentityProviderKind.AuthKit)]
    public async Task Mine_ThroughAnActor_ReturnsEmptyWithoutQuerying(IdentityProviderKind provider)
    {
        var service = new RecordingInvitations();
        var http = CreateHttp(service, TrustingOptions(provider));

        var result = await http.GetMyInvitations(Request(DelegatedClaims(provider), delegatedActor: true));

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeAssignableTo<IReadOnlyList<Invitation>>()!.ShouldBeEmpty();
        service.Calls.ShouldBeEmpty();
    }

    // Accepting and declining through an actor reach the service unverified, so it refuses them (403 EmailMismatch or
    // EmailNotVerified).
    [Theory]
    [InlineData(IdentityProviderKind.EntraId)]
    [InlineData(IdentityProviderKind.AuthKit)]
    public async Task AcceptAndDecline_ThroughAnActor_AreNotVerified(IdentityProviderKind provider)
    {
        var service = new RecordingInvitations();
        var http = CreateHttp(service, TrustingOptions(provider));

        await http.AcceptInvitation(Request(DelegatedClaims(provider), delegatedActor: true), "inv-1");
        service.LastInvitee.ShouldNotBeNull().IsEmailVerified.ShouldBeFalse();

        await http.DeclineInvitation(Request(DelegatedClaims(provider), delegatedActor: true), "inv-1");
        service.LastInvitee.ShouldNotBeNull().IsEmailVerified.ShouldBeFalse();

        service.Calls.ShouldBe(["accept:inv-1", "decline:inv-1"]);
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

    // Invitations are for users (spec 7.4): a machine is refused in every build, also one whose credentials carry every
    // scope and User.Impersonation, before the service is asked.
    [Fact]
    public async Task InvitationRoutes_Machine_IsAccessDeniedInEveryBuild()
    {
        var service = new RecordingInvitations();
        var http = CreateHttp(service);
        var body = FunctionTestDoubles.ToJsonBody(new InvitationCreate { Email = "ada@example.com", Role = AccountRole.Owner });

        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.GetInvitations(UserOnlyRoutes.Machine(), "acme.billing"));
        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.CreateInvitation(UserOnlyRoutes.Machine("POST", body), "acme.billing"));
        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.ResendInvitation(UserOnlyRoutes.Machine("POST"), "acme.billing", "inv-1"));
        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.RevokeInvitation(UserOnlyRoutes.Machine("DELETE"), "acme.billing", "inv-1"));
        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.GetMyInvitations(UserOnlyRoutes.Machine()));
        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.AcceptInvitation(UserOnlyRoutes.Machine("POST"), "inv-1"));
        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.DeclineInvitation(UserOnlyRoutes.Machine("POST"), "inv-1"));

        service.Calls.ShouldBeEmpty();
        service.LastCreated.ShouldBeNull();
        service.LastInvitee.ShouldBeNull();
    }

    private static InvitationsHttp CreateHttp(RecordingInvitations service, IdentityProviderKind provider = IdentityProviderKind.EntraId)
    {
        return CreateHttp(service, new UserAuthenticationOptions { Provider = provider });
    }

    private static InvitationsHttp CreateHttp(RecordingInvitations service, UserAuthenticationOptions options)
    {
        return new InvitationsHttp(service, Options.Create(options), FunctionTestDoubles.ApiJsonOptions);
    }

    // Options under which a direct token without email_verified counts as verified.
    private static UserAuthenticationOptions TrustingOptions(IdentityProviderKind provider)
    {
        return new UserAuthenticationOptions
        {
            Provider = provider,
            AuthKit = new AuthKitOptions { RequireVerifiedEmail = false },
        };
    }

    // A request with the claims; a body goes as JSON, with its content type unless contentType is false. A delegated
    // actor makes it a call through the actor mcp under NoSecretReveal, as the bearer middleware leaves a Storyteller
    // user token.
    private static TestHttpRequestData Request(List<Claim> claims, string? body = null, bool contentType = true, bool delegatedActor = false)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var headers = body is not null && contentType ? FunctionTestDoubles.JsonContentType() : null;
        var (context, request) = FunctionTestDoubles.CreateRequest(services, headers, body: body);
        claims.Add(new Claim("scp", Scopes.User.Impersonation));
        context.Items[FunctionContextItemKeys.CachedClaims] = claims;

        if (delegatedActor)
        {
            context.Items[FunctionContextItemKeys.DelegatedActor] = "mcp";
            context.Items[FunctionContextItemKeys.DelegatedRestrictions] = new[] { ActorRestrictions.NoSecretReveal };
        }

        return request;
    }

    // The claims of a Storyteller user token after the bearer middleware: the account id, and the name and user name
    // stored on the account; no email_verified.
    private static List<Claim> DelegatedClaims(IdentityProviderKind provider)
    {
        return
        [
            new("sub", provider == IdentityProviderKind.EntraId ? EntraAccountId : "user_01"),
            new("name", "Ada Lovelace"),
            new("preferred_username", "ada@example.com"),
        ];
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

        public Task<Invitation> DeclineInvitationAsync(string invitationId, InvitationIdentity invitee)
        {
            Calls.Add($"decline:{invitationId}");
            LastInvitee = invitee;
            return Task.FromResult(Invitation("acme.billing") with { Status = InvitationStatus.Declined });
        }

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
