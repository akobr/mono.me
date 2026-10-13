using System.Net;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.OpenApi;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.KeyValues.Audit;
using _42.Platform.Storyteller.KeyValues.Usage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.V1;

// The secret audit trail and the usage meters of a project. Administrators only.
public class KeyValueUsageHttp
{
    private const int DefaultUsageDays = 30;

    private readonly IKeyValueAuditLog _audit;
    private readonly IKeyValueUsageService _usage;
    private readonly IAccessService _access;
    private readonly TimeProvider _timeProvider;

    public KeyValueUsageHttp(IKeyValueAuditLog audit, IKeyValueUsageService usage, IAccessService access, TimeProvider? timeProvider = null)
    {
        _audit = audit;
        _usage = usage;
        _access = access;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    [Function(nameof(GetKeyValueAudit))]
    [OpenApiOperation(Definitions.RouteIds.KeyValues.GetKeyValueAudit, Definitions.Tags.KeyValues, Description = "The audit trail of the project's secrets, newest first: who revealed, wrote, deleted, restored, exported, imported, re-protected, changed the policy or the keys, and the aggregated binding reads. Never a value. Administrator only.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Only events of this view.")]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Only events of this key.")]
    [OpenApiParameter(Definitions.Parameters.Operation, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Only this operation (Reveal, Export, BindingRead, Create, Update, Delete, Restore, PermanentDelete, Purge, Reprotect, Import, PolicyChange, Rotate, Rewrap, ByokVerify, ByokUnavailable, RecipientAdded, RecipientChanged, RecipientRemoved).")]
    [OpenApiParameter(Definitions.Parameters.From, In = ParameterLocation.Query, Required = false, Type = typeof(DateTimeOffset), Description = "Only events at or after this time.")]
    [OpenApiParameter(Definitions.Parameters.To, In = ParameterLocation.Query, Required = false, Type = typeof(DateTimeOffset), Description = "Only events before this time.")]
    [OpenApiParameter(Definitions.Parameters.ContinuationToken, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.ContinuationToken)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(KeyValueAuditResponse), Description = "One page of events, newest first.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetKeyValueAudit(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.KeyValues.V1.Audit)]
        HttpRequestData request,
        string organization,
        string project)
    {
        await KeyValueAuthorization.CheckAdministratorAsync(request, _access, organization, project);

        var query = new KeyValueAuditQuery
        {
            View = request.Query[Definitions.Parameters.View],
            Key = request.Query[Definitions.Parameters.Key],
            Operation = KeyValueQuery.GetEnum<KeyValueAuditOperation>(request, Definitions.Parameters.Operation),
            From = KeyValueQuery.GetTime(request, Definitions.Parameters.From),
            To = KeyValueQuery.GetTime(request, Definitions.Parameters.To),
            ContinuationToken = request.Query[Definitions.Parameters.ContinuationToken],
        };

        var page = await _audit.QueryAsync(organization, project, query, request.FunctionContext.CancellationToken);
        return new OkObjectResult(page);
    }

    [Function(nameof(GetKeyValueUsage))]
    [OpenApiOperation(Definitions.RouteIds.KeyValues.GetKeyValueUsage, Definitions.Tags.KeyValues, Description = "The key/value usage of the project over a range of days (the last 30 by default, at most 400): the daily meters, their totals (key meters as key-months) and, when the deployment has prices, the estimated charges. Administrator only.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.From, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "The first day (yyyy-MM-dd); 30 days before 'to' by default.")]
    [OpenApiParameter(Definitions.Parameters.To, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "The last day (yyyy-MM-dd); today (UTC) by default.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(KeyValueUsageReport), Description = "The report.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The range is reversed or longer than 400 days.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetKeyValueUsage(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.KeyValues.V1.Usage)]
        HttpRequestData request,
        string organization,
        string project)
    {
        await KeyValueAuthorization.CheckAdministratorAsync(request, _access, organization, project);

        var to = KeyValueQuery.GetDate(request, Definitions.Parameters.To) ?? DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var from = KeyValueQuery.GetDate(request, Definitions.Parameters.From) ?? to.AddDays(-DefaultUsageDays);

        var report = await _usage.GetReportAsync(organization, project, from, to, request.FunctionContext.CancellationToken);
        return new OkObjectResult(report);
    }
}
