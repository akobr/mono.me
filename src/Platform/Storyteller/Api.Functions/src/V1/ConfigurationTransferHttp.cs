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
using Microsoft.OpenApi.Models;

using FromBodyAttribute = Microsoft.Azure.Functions.Worker.Http.FromBodyAttribute;

namespace _42.Platform.Storyteller.Api.V1;

// Export and import of the configurations of a view, as jobs processed in the background (configuration-transfer.md).
public class ConfigurationTransferHttp
{
    private readonly IConfigurationTransferService _transfers;
    private readonly IAccessService _access;
    private readonly ConfigurationTransferOptions _options;

    public ConfigurationTransferHttp(
        IConfigurationTransferService transfers,
        IAccessService access,
        IOptions<ConfigurationTransferOptions> options)
    {
        _transfers = transfers;
        _access = access;
        _options = options.Value;
    }

    [Function(nameof(StartConfigurationExport))]
    [OpenApiOperation(Definitions.RouteIds.Configuration.StartConfigurationExport, Definitions.Tags.Configurations, Description = "Starts an export of the own documents of every configuration in the view, with their versions, as a zip: a folder of JSON files or a git repository. Poll the export until it has succeeded, then download its archive.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(ConfigurationExportCreate), Description = "The format of the archive: Folder (default) or Git.")]
    [OpenApiResponseWithBody(HttpStatusCode.Accepted, Definitions.ContentTypes.Json, typeof(ConfigurationExport), Description = "The queued export; Location points to it.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseTransferLimit)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> StartConfigurationExport(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Configuration.V1.Exports)]
        HttpRequestData request,
        [FromBody] ConfigurationExportCreate? model,
        string organization,
        string project,
        string view)
    {
        request.CheckScope(Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        var format = model?.Format ?? ConfigurationArchiveFormat.Folder;

        if (!Enum.IsDefined(format))
        {
            return new BadRequestObjectResult(new ErrorResponse($"Unknown archive format '{format}'."));
        }

        var export = await _transfers.StartExportAsync(
            organization,
            project,
            view,
            format,
            request.GetAuthor(),
            request.GetAuditActor(),
            request.FunctionContext.CancellationToken);

        return new AcceptedResult(JobLocation(request, export.Id), export);
    }

    [Function(nameof(GetConfigurationExport))]
    [OpenApiOperation(Definitions.RouteIds.Configuration.GetConfigurationExport, Definitions.Tags.Configurations, Description = "The state of an export of the view.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Id, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.IdExport)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ConfigurationExport), Description = "The export.")]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The export doesn't exist in this view, or has expired.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetConfigurationExport(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Configuration.V1.Export)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string id)
    {
        request.CheckScope(Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        var export = await _transfers.GetExportAsync(organization, project, view, id, request.FunctionContext.CancellationToken)
                     ?? throw new NotFoundException($"The export '{id}' does not exist or has expired.");
        return new OkObjectResult(export);
    }

    [Function(nameof(GetConfigurationExportArchive))]
    [OpenApiOperation(Definitions.RouteIds.Configuration.GetConfigurationExportArchive, Definitions.Tags.Configurations, Description = "The zip of a succeeded export, named by Content-Disposition (the export's FileName).")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Id, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.IdExport)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Zip, typeof(byte[]), Description = "The archive.")]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The export doesn't exist in this view, or it or its archive has expired.")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The export is queued, running or failed (TransferNotReady).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetConfigurationExportArchive(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Configuration.V1.ExportArchive)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string id)
    {
        request.CheckScope(Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        var (export, archive) = await _transfers.OpenExportArchiveAsync(organization, project, view, id, request.FunctionContext.CancellationToken);
        return new FileStreamResult(archive, Definitions.ContentTypes.Zip) { FileDownloadName = export.FileName };
    }

    [Function(nameof(StartConfigurationImport))]
    [OpenApiOperation(Definitions.RouteIds.Configuration.StartConfigurationImport, Definitions.Tags.Configurations, Description = "Starts an import of a zip into the view: every <annotation-key>.json file, in any folder, replaces the annotation's own document as a new version unless it is equal. Version files, export.json and .git are ignored. Poll the import for the outcome of each file.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Force, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = "When true, store documents even if they violate their schema.")]
    [OpenApiParameter(Definitions.Parameters.DryRun, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = "When true, validate and compare only; nothing is written.")]
    [OpenApiParameter(Definitions.Parameters.FileName, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "The name of the uploaded file, for display.")]
    [OpenApiRequestBody(Definitions.ContentTypes.Zip, typeof(byte[]), Required = true, Description = "The zip archive.")]
    [OpenApiResponseWithBody(HttpStatusCode.Accepted, Definitions.ContentTypes.Json, typeof(ConfigurationImport), Description = "The queued import; Location points to it.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The body is not a zip, or breaks a limit of the archive (InvalidArchive).")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseTransferLimit)]
    [OpenApiResponseWithBody(HttpStatusCode.RequestEntityTooLarge, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The archive is larger than allowed (ArchiveTooLarge).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> StartConfigurationImport(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Configuration.V1.Imports)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        [FromQuery] bool force = false,
        [FromQuery] bool dryRun = false,
        [FromQuery] string? fileName = null)
    {
        request.CheckScope(Scopes.Configuration.Write, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Contributor);

        var cancellationToken = request.FunctionContext.CancellationToken;
        using var archive = new MemoryStream();

        if (request.Headers.TryGetValues("Content-Length", out var lengths)
            && long.TryParse(lengths.FirstOrDefault(), out var length)
            && length > _options.MaxArchiveBytes)
        {
            return TooLarge();
        }

        if (!await TryCopyWithLimitAsync(request.Body, archive, _options.MaxArchiveBytes, cancellationToken))
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
            force,
            dryRun,
            request.GetAuthor(),
            request.GetAuditActor(),
            cancellationToken);

        return new AcceptedResult(JobLocation(request, import.Id), import);
    }

    [Function(nameof(GetConfigurationImport))]
    [OpenApiOperation(Definitions.RouteIds.Configuration.GetConfigurationImport, Definitions.Tags.Configurations, Description = "The state of an import into the view, with the outcome of its files.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Id, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.IdImport)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ConfigurationImport), Description = "The import.")]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The import doesn't exist in this view, or has expired.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetConfigurationImport(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Configuration.V1.Import)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string id)
    {
        request.CheckScope(Scopes.Configuration.Write, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Contributor);

        var import = await _transfers.GetImportAsync(organization, project, view, id, request.FunctionContext.CancellationToken)
                     ?? throw new NotFoundException($"The import '{id}' does not exist or has expired.");
        return new OkObjectResult(import);
    }

    // The job below the collection the request was posted to.
    private static string JobLocation(HttpRequestData request, string id)
    {
        return $"{request.Url.GetLeftPart(UriPartial.Path).TrimEnd('/')}/{id}";
    }

    private ObjectResult TooLarge()
    {
        return new ObjectResult(new ErrorResponse($"The archive is larger than {_options.MaxArchiveBytes} bytes.") { ErrorCode = ErrorCodes.ArchiveTooLarge })
        {
            StatusCode = (int)HttpStatusCode.RequestEntityTooLarge,
        };
    }

    private static async Task<bool> TryCopyWithLimitAsync(Stream source, Stream target, long limit, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (target.Length + read > limit)
            {
                return false;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return true;
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
}
