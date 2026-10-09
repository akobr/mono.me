using System.Net;
using System.Security.Claims;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.OpenApi;
using _42.Platform.Storyteller.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

using FromBodyAttribute = Microsoft.Azure.Functions.Worker.Http.FromBodyAttribute;

namespace _42.Platform.Storyteller.Api.V1;

// Invitations of email addresses to organizations and projects. Administrators manage them per access point;
// the invitee lists, accepts and declines them with a token whose verified email matches the invitation.
public class InvitationsHttp
{
    private const string EmailVerifiedClaimType = "email_verified";

    private static readonly string[] EmailClaimTypes = ["preferred_username", "email", "unique_name", ClaimTypes.Upn];

    private readonly IInvitationService _invitations;
    private readonly UserAuthenticationOptions _authOptions;
    private readonly IUserProfileResolver? _profileResolver;

    public InvitationsHttp(
        IInvitationService invitations,
        IOptions<UserAuthenticationOptions> authOptions,
        IUserProfileResolver? profileResolver = null)
    {
        _invitations = invitations;
        _authOptions = authOptions.Value;
        _profileResolver = profileResolver;
    }

    [Function(nameof(GetInvitations))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetInvitations, Definitions.Tags.Access, Description = "Invitations to the organization or project, newest first. Needs Administrator.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AccessPointKey)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(IEnumerable<Invitation>), Description = "The invitations. A pending invitation past its expiry has the status Expired.")]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The access point doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetInvitations(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Access.V1.PointInvitations)]
        HttpRequestData request,
        string key)
    {
        request.CheckScope(Scopes.User.Impersonation);
        var invitations = await _invitations.GetInvitationsAsync(MembersHttp.NormalizeKey(key), request.GetIdentityUniqueId());
        return new OkObjectResult(invitations);
    }

    [Function(nameof(CreateInvitation))]
    [OpenApiOperation(Definitions.RouteIds.Access.CreateInvitation, Definitions.Tags.Access, Description = "Invites an email address with a role. Needs Administrator; Owner to offer Owner. With AuthKit and a management key, WorkOS sends the email.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AccessPointKey)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(InvitationCreate), Description = "The email, the role, and optionally the validity in days (1 to 30, 7 by default).")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(Invitation), Description = "The pending invitation. IsEmailSent is false when no email went out; share the link instead.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "Not an email address, the role None, or an invalid validity.")]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The access point doesn't exist.")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "A pending invitation for the email already exists (InvitationExists).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> CreateInvitation(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Access.V1.PointInvitations)]
        HttpRequestData request,
        [FromBody] InvitationCreate? model,
        string key)
    {
        request.CheckScope(Scopes.User.Impersonation);

        if (model is null)
        {
            return new BadRequestObjectResult(new ErrorResponse("The invitation is missing."));
        }

        try
        {
            var invitation = await _invitations.CreateInvitationAsync(
                MembersHttp.NormalizeKey(key),
                model,
                request.GetIdentityUniqueId(),
                request.GetClaim("name"));
            return new OkObjectResult(invitation);
        }
        catch (ArgumentException exception)
        {
            return new BadRequestObjectResult(new ErrorResponse(exception.Message));
        }
    }

    [Function(nameof(ResendInvitation))]
    [OpenApiOperation(Definitions.RouteIds.Access.ResendInvitation, Definitions.Tags.Access, Description = "Sends the invitation email again and restarts its validity. Needs Administrator.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AccessPointKey)]
    [OpenApiParameter(Definitions.Parameters.Id, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.InvitationId)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(Invitation), Description = "The renewed invitation.")]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The invitation doesn't exist in this access point.")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The invitation was already answered or revoked (InvitationNotPending).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> ResendInvitation(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Access.V1.PointInvitationResend)]
        HttpRequestData request,
        string key,
        string id)
    {
        request.CheckScope(Scopes.User.Impersonation);
        var invitation = await _invitations.ResendInvitationAsync(MembersHttp.NormalizeKey(key), id, request.GetIdentityUniqueId());
        return new OkObjectResult(invitation);
    }

    [Function(nameof(RevokeInvitation))]
    [OpenApiOperation(Definitions.RouteIds.Access.RevokeInvitation, Definitions.Tags.Access, Description = "Revokes a pending invitation. Needs Administrator.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AccessPointKey)]
    [OpenApiParameter(Definitions.Parameters.Id, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.InvitationId)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(Invitation), Description = "The revoked invitation.")]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The invitation doesn't exist in this access point.")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The invitation was already answered or revoked (InvitationNotPending).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> RevokeInvitation(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Delete, Route = Definitions.Routes.Access.V1.PointInvitation)]
        HttpRequestData request,
        string key,
        string id)
    {
        request.CheckScope(Scopes.User.Impersonation);
        var invitation = await _invitations.RevokeInvitationAsync(MembersHttp.NormalizeKey(key), id, request.GetIdentityUniqueId());
        return new OkObjectResult(invitation);
    }

    [Function(nameof(GetMyInvitations))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetMyInvitations, Definitions.Tags.Access, Description = "Pending invitations for the email of the signed-in user. Works before the account exists.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(IEnumerable<Invitation>), Description = "Pending, unexpired invitations; empty when the token has no email.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetMyInvitations(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Access.V1.MyInvitations)]
        HttpRequestData request)
    {
        request.CheckScope(Scopes.User.Impersonation);
        var email = request.GetClaim(EmailClaimTypes);
        IReadOnlyList<Invitation> invitations = email is null
            ? []
            : await _invitations.GetPendingInvitationsAsync(email);
        return new OkObjectResult(invitations);
    }

    [Function(nameof(AcceptInvitation))]
    [OpenApiOperation(Definitions.RouteIds.Access.AcceptInvitation, Definitions.Tags.Access, Description = "Accepts the invitation: creates the account when needed and grants the role. The verified email of the token must match.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Id, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.InvitationId)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(Account), Description = "The account of the signed-in user, with the new membership.")]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The invitation doesn't exist.")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The invitation is not pending (InvitationNotPending) or has expired (InvitationExpired).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> AcceptInvitation(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Access.V1.InvitationAccept)]
        HttpRequestData request,
        string id)
    {
        request.CheckScope(Scopes.User.Impersonation);
        var account = await _invitations.AcceptInvitationAsync(id, await ResolveInviteeAsync(request));
        return new OkObjectResult(account);
    }

    [Function(nameof(DeclineInvitation))]
    [OpenApiOperation(Definitions.RouteIds.Access.DeclineInvitation, Definitions.Tags.Access, Description = "Declines the invitation. The verified email of the token must match.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Id, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.InvitationId)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(Invitation), Description = "The declined invitation.")]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The invitation doesn't exist.")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The invitation was already answered or revoked (InvitationNotPending).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> DeclineInvitation(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Access.V1.InvitationDecline)]
        HttpRequestData request,
        string id)
    {
        request.CheckScope(Scopes.User.Impersonation);
        var invitation = await _invitations.DeclineInvitationAsync(id, await ResolveInviteeAsync(request));
        return new OkObjectResult(invitation);
    }

    // email_verified decides when the token carries it. Without it, AuthKit tokens count as verified only
    // when RequireVerifiedEmail is off; Entra ID has no such claim and its directory names are trusted.
    internal static bool IsEmailVerified(HttpRequestData request, UserAuthenticationOptions options)
    {
        var claim = request.GetClaim(EmailVerifiedClaimType);

        if (claim is not null)
        {
            return bool.TryParse(claim, out var verified) && verified;
        }

        return options.Provider != IdentityProviderKind.AuthKit || !options.AuthKit.RequireVerifiedEmail;
    }

    private async Task<InvitationIdentity> ResolveInviteeAsync(HttpRequestData request)
    {
        string? userName = null;
        string? name = null;

        try
        {
            (userName, name) = await request.GetIdentityProfileAsync(_profileResolver);
        }
        catch (SecurityTokenException)
        {
            // Names are only display data for a new account; the email decides below.
        }

        return new InvitationIdentity(
            request.GetIdentityUniqueId(),
            request.GetClaim(EmailClaimTypes),
            IsEmailVerified(request, _authOptions),
            userName,
            name);
    }
}
