using System.Net;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

using FromBodyAttribute = Microsoft.Azure.Functions.Worker.Http.FromBodyAttribute;

namespace _42.Platform.Storyteller.Api.V1;

public class CertificatesHttp
{
    private readonly IAccessService _accessService;
    private readonly ICertificateRenewalService? _renewalService;
    private readonly SharedCertificateService? _sharedCertService;
    private readonly ILogger<CertificatesHttp> _logger;

    public CertificatesHttp(
        IAccessService accessService,
        ILogger<CertificatesHttp> logger,
        ICertificateRenewalService? renewalService = null,
        SharedCertificateService? sharedCertService = null)
    {
        _accessService = accessService;
        _logger = logger;
        _renewalService = renewalService;
        _sharedCertService = sharedCertService;
    }

    [Function(nameof(PostRenewCertificate))]
    [OpenApiOperation(Definitions.RouteIds.Access.RenewMachineCertificate, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Mtls, SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "X-ARR-ClientCert", Description = "Machine certificate authentication via mTLS. Mutual TLS client certificate authentication. The client certificate is presented during the TLS handshake (production) or via the X-ARR-ClientCert header (development). Configure the certificate on HttpClientHandler or equivalent, this is a transport-layer concern, not an API parameter.")]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.Id, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.IdMachine)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(CertificateRenewalResult), Description = "The renewal result with the new certificate or an already-renewed message.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Not in renewal window.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Machine identity mismatch or missing certificate.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "Machine access not found.")]
    public async Task<IActionResult> PostRenewCertificate(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Access.V1.MachineCertificateRenew)]
        HttpRequestData request,
        string organization,
        string project,
        string id)
    {
        // The machine must be authenticated by its own certificate.
        if (!request.FunctionContext.Items.TryGetValue(FunctionContextItemKeys.MachineIdentity, out var identity)
            || identity is not string machineId)
        {
            return new UnauthorizedResult();
        }

        // Route {id} must match the authenticated machine identity.
        if (!string.Equals(machineId, id, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Renewal attempt: route id {RouteId} does not match authenticated identity {MachineId}",
                id,
                machineId);
            return new UnauthorizedResult();
        }

        // Get the presenting certificate's thumbprint (set by middleware).
        if (!request.FunctionContext.Items.TryGetValue(FunctionContextItemKeys.PresentingCertificateThumbprint, out var thumbObj)
            || thumbObj is not string presentingThumbprint)
        {
            return new UnauthorizedResult();
        }

        if (_renewalService is null)
        {
            return new StatusCodeResult((int)HttpStatusCode.NotImplemented);
        }

        var result = await _renewalService.RenewAsync(organization, project, id, presentingThumbprint);

        return result.Outcome switch
        {
            CertificateRenewalOutcome.Success => new OkObjectResult(new
            {
                result.Thumbprint,
                Certificate = result.Pkcs12 is not null ? Convert.ToBase64String(result.Pkcs12) : null,
                CertificatePassword = result.Password,
                result.LastRenewalAt,
            }),
            CertificateRenewalOutcome.AlreadyRenewed => new OkObjectResult(new
            {
                result.Message,
                result.LastRenewalAt,
            }),
            CertificateRenewalOutcome.NotInRenewalWindow => new BadRequestObjectResult(new ErrorResponse
            {
                Message = result.Message ?? "Not in renewal window.",
            }),
            CertificateRenewalOutcome.NotFound => new NotFoundObjectResult(new ErrorResponse
            {
                Message = result.Message ?? "Machine access not found.",
            }),
            _ => new StatusCodeResult((int)HttpStatusCode.InternalServerError),
        };
    }

    [Function(nameof(GetSharedCertificates))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetSharedCertificates, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(IEnumerable<SharedCertificate>), Description = "The list of shared certificates for the project.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    public async Task<IActionResult> GetSharedCertificates(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Access.V1.SharedCertificates)]
        HttpRequestData request,
        string organization,
        string project)
    {
        request.CheckScope(Scopes.User.Impersonation);
        await request.CheckAccessToProjectAsync(_accessService, organization, project, AccountRole.Administrator);

        if (_sharedCertService is null)
        {
            return new StatusCodeResult((int)HttpStatusCode.NotImplemented);
        }

        var certificates = await _sharedCertService.ListAsync(organization, project);
        return new OkObjectResult(certificates);
    }

    [Function(nameof(PostSharedCertificate))]
    [OpenApiOperation(Definitions.RouteIds.Access.IssueSharedCertificate, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(SharedCertificateCreate), Description = "The label and optional lifetime for the shared certificate.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(SharedCertificate), Description = "The issued shared certificate with PKCS#12 data.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    public async Task<IActionResult> PostSharedCertificate(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Access.V1.SharedCertificates)]
        HttpRequestData request,
        [FromBody] SharedCertificateCreate model,
        string organization,
        string project)
    {
        request.CheckScope(Scopes.User.Impersonation);
        await request.CheckAccessToProjectAsync(_accessService, organization, project, AccountRole.Administrator);

        if (_sharedCertService is null)
        {
            return new StatusCodeResult((int)HttpStatusCode.NotImplemented);
        }

        if (!CertificateIdentity.IsValidSpiffePathSegment(model.Label))
        {
            return new BadRequestObjectResult(new ErrorResponse { Message = "Invalid certificate label. Only letters, digits, hyphens, underscores, and dots are allowed." });
        }

        var certificate = await _sharedCertService.IssueAsync(organization, project, model.Label, model.LifetimeDays);
        return new OkObjectResult(certificate);
    }

    [Function(nameof(DeleteSharedCertificate))]
    [OpenApiOperation(Definitions.RouteIds.Access.RevokeSharedCertificate, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.Thumbprint, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The thumbprint of the shared certificate to revoke.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.OK, Description = "The shared certificate was revoked.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The shared certificate was not found.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    public async Task<IActionResult> DeleteSharedCertificate(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Delete, Route = Definitions.Routes.Access.V1.SharedCertificate)]
        HttpRequestData request,
        string organization,
        string project,
        string thumbprint)
    {
        request.CheckScope(Scopes.User.Impersonation);
        await request.CheckAccessToProjectAsync(_accessService, organization, project, AccountRole.Administrator);

        if (_sharedCertService is null)
        {
            return new StatusCodeResult((int)HttpStatusCode.NotImplemented);
        }

        var revoked = await _sharedCertService.RevokeAsync(organization, project, thumbprint);

        return revoked
            ? new OkResult()
            : new NotFoundResult();
    }
}
