using System.Net;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Api.Exhibitions;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.OpenApi;
using _42.Platform.Storyteller.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.V1;

// The exhibition: the demo project of the deployment (section Exhibition), which any user may join as a Reader. The
// role and the key never come from the request: JoinAccessPointAsync is trusted, and the join gives exactly Reader on
// exactly the configured project, and no role in its organization.
public class ExhibitionHttp
{
    // The join is tried again this many times after a Conflict of the service, each time after a delay between these.
    private const int JoinRetries = 3;
    private const int MinRetryDelayMilliseconds = 50;
    private const int MaxRetryDelayMilliseconds = 250;

    private const string OrganizationSetting = $"{ExhibitionOptions.SectionName}:{nameof(ExhibitionOptions.Organization)}";
    private const string ProjectSetting = $"{ExhibitionOptions.SectionName}:{nameof(ExhibitionOptions.Project)}";
    private const string OwnerSetting = $"{ExhibitionOptions.SectionName}:{nameof(ExhibitionOptions.Owner)}";

    // The warnings of a configured exhibition that is not offered: they name the settings, never their values.
    private const string ProjectMissingWarning = $"The exhibition is not offered: the project of {OrganizationSetting} and {ProjectSetting} doesn't exist. Seed the exhibition, or correct the settings.";
    private const string NotOwnedWarning = $"The exhibition is not offered: the account of {OwnerSetting} is not the Owner of the project of {OrganizationSetting} and {ProjectSetting}. Seed the exhibition with that owner, or correct the settings.";
    private const string NotAloneWarning = $"The exhibition is not offered: the account of {OwnerSetting} is not the only Administrator or Owner of the project of {OrganizationSetting} and {ProjectSetting}. Remove the other Administrators and Owners or lower their roles, or correct the settings.";

    private readonly IAccessService _accessService;
    private readonly IUserProfileResolver? _profileResolver;
    private readonly ExhibitionOptions _options;
    private readonly UserAuthenticationOptions _authOptions;
    private readonly ILogger<ExhibitionHttp> _logger;

    public ExhibitionHttp(
        IAccessService accessService,
        ILogger<ExhibitionHttp> logger,
        IUserProfileResolver? profileResolver = null,
        IOptions<ExhibitionOptions>? exhibitionOptions = null,
        IOptions<UserAuthenticationOptions>? authOptions = null)
    {
        _accessService = accessService;
        _logger = logger;
        _profileResolver = profileResolver;
        _options = exhibitionOptions?.Value ?? new ExhibitionOptions();
        _authOptions = authOptions?.Value ?? new UserAuthenticationOptions();
    }

    // The wait before the join is tried again; the tests replace it.
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    [Function(nameof(GetExhibition))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetExhibition, Definitions.Tags.Access, Description = "The exhibition: the demo project of the deployment, which the caller may join as a Reader. Its view is default. Works before the account exists.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(Models.Exhibition), Description = "The organization and the project of the exhibition, and the description of the project.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The deployment has no exhibition.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetExhibition(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Access.V1.Exhibition)]
        HttpRequestData request)
    {
        request.EnsureUser();
        var exhibition = await FindExhibitionAsync();

        return exhibition is null
            ? new NotFoundResult()
            : new OkObjectResult(exhibition);
    }

    [Function(nameof(JoinExhibition))]
    [OpenApiOperation(Definitions.RouteIds.Access.JoinExhibition, Definitions.Tags.Access, Description = "Makes the caller a Reader of the exhibition project: creates the account, without an organization, when needed, and keeps a higher role. No request body.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(Account), Description = "The account of the signed-in user, with the membership of the exhibition project.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The deployment has no exhibition.")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The members of the exhibition project changed concurrently too many times (Conflict); try again.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> JoinExhibition(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Access.V1.ExhibitionJoin)]
        HttpRequestData request)
    {
        request.EnsureUser();

        // Before anything is written: nothing is created for an exhibition that cannot be joined.
        if (await FindExhibitionAsync() is null)
        {
            return new NotFoundResult();
        }

        // The id a registration uses, as PostAccount and the acceptance of an invitation. The account and the membership
        // are two writes: a join that fails after its account was created leaves an account without memberships, and a
        // second call joins and creates nothing.
        var accountId = request.GetRegistrationAccountId(_authOptions.Provider);
        await EnsureAccountAsync(request, accountId);
        await JoinAsync(accountId, request.FunctionContext.CancellationToken);

        // The account with the membership.
        var account = await _accessService.GetAccountAsync(accountId);
        return new OkObjectResult(account);
    }

    // The exhibition of a request, or null (404 without a body). A deployment without the settings has none, and nothing
    // is logged: every newcomer of such a deployment asks. With the settings, the project must exist, Exhibition:Owner
    // must be its Owner, and no other account may be its Administrator or Owner; otherwise the deployment is
    // misconfigured, or not seeded yet, which a Warning says by the names of the settings.
    //
    // Owning is not creating: an Owner makes any existing account an Owner without its consent (POST v1/access/grant).
    // Whoever registers the names before the seed could so add the configured account to a project of their own, and
    // by the owner's role alone that project would be offered while they manage it: they would list every joiner's
    // name and email, and change the description at will. They have to leave the project first. What they wrote before
    // (the description, the content) stays: the access map does not say who created a project.
    private async Task<Models.Exhibition?> FindExhibitionAsync()
    {
        if (!_options.IsConfigured)
        {
            return null;
        }

        var project = await _accessService.GetAccessPointAsync(_options.ProjectKey);

        if (project is null)
        {
            _logger.LogWarning(ProjectMissingWarning);
            return null;
        }

        if (!project.AccessMap.TryGetValue(_options.Owner!, out var ownerRole) || ownerRole != AccountRole.Owner)
        {
            _logger.LogWarning(NotOwnedWarning);
            return null;
        }

        if (project.AccessMap.Any(member => member.Value >= AccountRole.Administrator && !string.Equals(member.Key, _options.Owner, StringComparison.Ordinal)))
        {
            _logger.LogWarning(NotAloneWarning);
            return null;
        }

        return new Models.Exhibition
        {
            Organization = _options.Organization!,
            Project = _options.Project!,
            Description = project.Description,
        };
    }

    // A caller without an account gets one without an organization, with the names of the token. A token without the
    // names is 401, as for PostAccount, and nothing is created: an account with empty names would be a blank row of
    // the member list.
    private async Task EnsureAccountAsync(HttpRequestData request, string accountId)
    {
        if (await _accessService.GetAccountAsync(accountId) is not null)
        {
            return;
        }

        var (userName, name) = await request.GetIdentityProfileAsync(_profileResolver);

        try
        {
            await _accessService.CreateAccountAsync(new AccountCreate { IdentityId = accountId, UserName = userName, Name = name });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A concurrent request of the caller created the account meanwhile: the service's own read found it
            // (AccountExists), or its write met the other one. Only a create that left no account has failed.
            if (!await HasAccountAfterFailedCreateAsync(accountId))
            {
                throw;
            }
        }
    }

    // The read after a create that threw. When it fails too (the store is not reachable, or throttles), it says nothing
    // about the account: the failure of the create goes out, not the one of this read.
    private async Task<bool> HasAccountAfterFailedCreateAsync(string accountId)
    {
        try
        {
            return await _accessService.GetAccountAsync(accountId) is not null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    // Idempotent, and a higher role stays. Every newcomer writes the same document, and the service gives up after its
    // second attempt, which is sized for an administrator's change: its Conflict is tried again, each time after a short
    // random delay, and goes out as the 409 after the last attempt.
    private async Task JoinAsync(string accountId, CancellationToken cancellationToken)
    {
        for (var retry = 0; ; retry++)
        {
            try
            {
                await _accessService.JoinAccessPointAsync(_options.ProjectKey, accountId, AccountRole.Reader);
                return;
            }
            catch (ConflictException exception) when (exception.ErrorCode == ErrorCodes.Conflict && retry < JoinRetries)
            {
                await Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(MinRetryDelayMilliseconds, MaxRetryDelayMilliseconds + 1)), cancellationToken);
            }
        }
    }
}
