using System.Net;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.OpenApi;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Transferring;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.V1;

// Export and import of the documents of a view (annotations, schemas, templates and configurations), as jobs processed
// in the background (export-import.md). Users need the Administrator role on the project on every route, also to read a
// job (424-66); machines have no role and need the scopes of the included kinds (ViewTransferScopes), as user tokens do.
public class ViewTransferHttp
{
    private readonly IViewTransferService _transfers;
    private readonly IAccessService _access;
    private readonly ViewTransferOptions _options;

    public ViewTransferHttp(
        IViewTransferService transfers,
        IAccessService access,
        IOptions<ViewTransferOptions> options)
    {
        _transfers = transfers;
        _access = access;
        _options = options.Value;
    }

    // Debug builds skip the scope checks (DEV_AUTH), as CheckScope does; the tests turn them on to cover the rules per kind.
    internal bool ChecksScopes { get; init; } = !Security.HttpRequestDataExtensions.IsDevelopmentAuthentication;

    [Function(nameof(StartViewExport))]
    [OpenApiOperation(Definitions.RouteIds.Transfers.StartViewExport, Definitions.Tags.Transfers, Description = "Starts an export of the view as a zip: its annotations, schemas, templates and configurations, or the kinds Include names. Folder (default) writes the current documents and the earlier versions as JSON files, Git a repository with every version as a commit. Secrets, effective documents, the audit and deleted annotations are never exported. Poll the export until it has succeeded, then download its archive.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(ViewExportCreate), Description = "The format of the archive, Folder (default) or Git, and the kinds to include (Annotations, Schemas, Templates, Configurations); none means all four.")]
    [OpenApiResponseWithBody(HttpStatusCode.Accepted, Definitions.ContentTypes.Json, typeof(ViewExport), Description = "The queued export, with its Include resolved; Location points to it.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The body is not a JSON object, or names an unknown format or kind.")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseTransferLimit)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthenticated)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseTransferForbiddenExport)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> StartViewExport(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Transfers.V1.Exports)]
        HttpRequestData request,
        string organization,
        string project,
        string view)
    {
        var cancellationToken = request.FunctionContext.CancellationToken;

        // read here rather than bound, so an unknown format or kind is a 400 that names it
        using var reader = new StreamReader(request.Body);

        if (!ViewTransferRequests.TryParseExport(await reader.ReadToEndAsync(cancellationToken), out var model, out var error))
        {
            return new BadRequestObjectResult(new ErrorResponse(error));
        }

        CheckScopes(request, model.Include, ViewTransferKind.Export);
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Administrator);

        var export = await _transfers.StartExportAsync(
            organization,
            project,
            view,
            model.Format,
            model.Include,
            request.GetAuditActor(),
            cancellationToken);

        return new AcceptedResult(JobLocation(request, export.Id), export);
    }

    [Function(nameof(GetViewExport))]
    [OpenApiOperation(Definitions.RouteIds.Transfers.GetViewExport, Definitions.Tags.Transfers, Description = "The state of an export of the view, with the counts of the exported kinds.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Id, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.IdExport)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ViewExport), Description = "The export.")]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The export doesn't exist in this view, or has expired.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthenticated)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseTransferForbiddenExport)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetViewExport(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Transfers.V1.Export)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string id)
    {
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Administrator);

        var export = await GetExportAsync(request, organization, project, view, id);
        return new OkObjectResult(export);
    }

    [Function(nameof(GetViewExportArchive))]
    [OpenApiOperation(Definitions.RouteIds.Transfers.GetViewExportArchive, Definitions.Tags.Transfers, Description = "The zip of a succeeded export, named by Content-Disposition (the export's FileName).")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Id, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.IdExport)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Zip, typeof(byte[]), Description = "The archive.")]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The export doesn't exist in this view, or it or its archive has expired.")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The export is queued, running or failed (TransferNotReady).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthenticated)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseTransferForbiddenExport)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetViewExportArchive(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Transfers.V1.ExportArchive)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string id)
    {
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Administrator);

        // the scopes of the export's kinds are checked before its state or archive tells anything
        await GetExportAsync(request, organization, project, view, id);

        var (export, archive) = await _transfers.OpenExportArchiveAsync(organization, project, view, id, request.FunctionContext.CancellationToken);
        return new FileStreamResult(archive, Definitions.ContentTypes.Zip) { FileDownloadName = export.FileName };
    }

    [Function(nameof(StartViewImport))]
    [OpenApiOperation(Definitions.RouteIds.Transfers.StartViewImport, Definitions.Tags.Transfers, Description = "Starts an import of a zip into the view. Each file is recognized by its name, in any folder: <key>.json is an annotation, <key>.conf.json a configuration, <type>.template.json a template, and <type>.schema.json, <key>.schema.json and <key>.<type>.schema.json are schemas. Version files, export.json and hidden paths are ignored. The included kinds are written in the order annotations, schemas, templates, configurations; nothing is deleted. Poll the import for the outcome of each file.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Include, In = ParameterLocation.Query, Required = false, Type = typeof(ViewArchiveContent[]), Explode = false, Description = Definitions.Descriptions.TransferInclude)]
    [OpenApiParameter(Definitions.Parameters.Existing, In = ParameterLocation.Query, Required = false, Type = typeof(ViewImportExisting), Description = Definitions.Descriptions.TransferExisting)]
    [OpenApiParameter(Definitions.Parameters.Force, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = "When true, store schemas and configurations that break each other, with the warning SchemaViolation. Invalid JSON, invalid JSON Schemas, broken annotation rules and broken directives still fail.")]
    [OpenApiParameter(Definitions.Parameters.DryRun, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = "When true, decide every outcome and warning without writing anything.")]
    [OpenApiParameter(Definitions.Parameters.FileName, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "The name of the uploaded file, for display.")]
    [OpenApiRequestBody(Definitions.ContentTypes.Zip, typeof(byte[]), Required = true, Description = "The zip archive.")]
    [OpenApiResponseWithBody(HttpStatusCode.Accepted, Definitions.ContentTypes.Json, typeof(ViewImport), Description = "The queued import, with its Include resolved; Location points to it.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "An unknown kind in include or an unknown existing value (InvalidQuery), or the body is not a zip or breaks a limit of the archive (InvalidArchive).")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseTransferLimit)]
    [OpenApiResponseWithBody(HttpStatusCode.RequestEntityTooLarge, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The archive is larger than allowed (ArchiveTooLarge).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthenticated)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseTransferForbiddenImport)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> StartViewImport(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Transfers.V1.Imports)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        [FromQuery] bool force = false,
        [FromQuery] bool dryRun = false,
        [FromQuery] string? fileName = null)
    {
        // read from the query itself: a repeated include (a list, as some clients send it) arrives joined by commas
        var include = ViewTransferRequests.ParseInclude(request.Query[Definitions.Parameters.Include]);
        var existing = ViewTransferRequests.ParseExisting(request.Query[Definitions.Parameters.Existing]);

        CheckScopes(request, include, ViewTransferKind.Import);
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Administrator);

        var cancellationToken = request.FunctionContext.CancellationToken;
        using var archive = new MemoryStream();

        if (!await request.TryCopyBodyAsync(archive, _options.MaxArchiveBytes, cancellationToken))
        {
            return TooLarge();
        }

        if (!IsZip(archive))
        {
            return new BadRequestObjectResult(new ErrorResponse("The body is not a zip archive.") { ErrorCode = ErrorCodes.InvalidArchive });
        }

        archive.Position = 0;
        var import = await _transfers.StartImportAsync(
            organization,
            project,
            view,
            archive,
            fileName,
            include,
            existing,
            force,
            dryRun,
            request.GetAuditActor(),
            cancellationToken);

        return new AcceptedResult(JobLocation(request, import.Id), import);
    }

    [Function(nameof(GetViewImport))]
    [OpenApiOperation(Definitions.RouteIds.Transfers.GetViewImport, Definitions.Tags.Transfers, Description = "The state of an import into the view, with a summary per included kind, the total, and the outcome and warnings of its files.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Id, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.IdImport)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ViewImport), Description = "The import.")]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The import doesn't exist in this view, or has expired.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthenticated)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseTransferForbiddenImport)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetViewImport(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Transfers.V1.Import)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string id)
    {
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Administrator);

        var import = await _transfers.GetImportAsync(organization, project, view, id, request.FunctionContext.CancellationToken)
                     ?? throw new NotFoundException($"The import '{id}' does not exist or has expired.");
        CheckScopes(request, import.Include, ViewTransferKind.Import, isJob: true);
        return new OkObjectResult(import);
    }

    // The job below the collection the request was posted to.
    private static string JobLocation(HttpRequestData request, string id)
    {
        return $"{request.Url.GetLeftPart(UriPartial.Path).TrimEnd('/')}/{id}";
    }

    // A zip starts with a local file header, or with the end of the central directory when it is empty.
    private static bool IsZip(MemoryStream archive)
    {
        var bytes = archive.GetBuffer();
        return archive.Length >= 4
               && bytes[0] == 'P'
               && bytes[1] == 'K'
               && ((bytes[2] == 3 && bytes[3] == 4) || (bytes[2] == 5 && bytes[3] == 6));
    }

    // An export of this view that the caller may read: the scopes of the kinds it includes.
    private async Task<ViewExport> GetExportAsync(HttpRequestData request, string organization, string project, string view, string id)
    {
        var export = await _transfers.GetExportAsync(organization, project, view, id, request.FunctionContext.CancellationToken)
                     ?? throw new NotFoundException($"The export '{id}' does not exist or has expired.");
        CheckScopes(request, export.Include, ViewTransferKind.Export, isJob: true);
        return export;
    }

    // The scopes of every included kind (null or empty: all four); a stored job has its include resolved. A request
    // without credentials has no scopes at all: it is 401, as on every route, not a 403 that asks it to change include.
    private void CheckScopes(HttpRequestData request, IReadOnlyList<ViewArchiveContent>? include, ViewTransferKind kind, bool isJob = false)
    {
        if (!ChecksScopes)
        {
            return;
        }

        if (request.GetClaims().Count == 0)
        {
            throw new SecurityTokenException("Missing credentials.");
        }

        ViewTransferScopes.Ensure(request.GetScopes(), include, kind, isJob);
    }

    private ObjectResult TooLarge()
    {
        return new ObjectResult(new ErrorResponse($"The archive is larger than {_options.MaxArchiveBytes} bytes.") { ErrorCode = ErrorCodes.ArchiveTooLarge })
        {
            StatusCode = (int)HttpStatusCode.RequestEntityTooLarge,
        };
    }
}
