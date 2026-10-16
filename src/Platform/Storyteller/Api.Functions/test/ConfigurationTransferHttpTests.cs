using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.Transfers;
using _42.Platform.Storyteller.Api.V1;
using _42.Platform.Storyteller.Transferring;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class ConfigurationTransferHttpTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 1, 30, 12, TimeSpan.Zero);

    private readonly RecordingTransfers _transfers = new();

    [Fact]
    public async Task StartExport_AnswersAcceptedWithTheJobLocation()
    {
        var http = Http();
        var request = Request(url: "https://host/api/v1/acme/web/default/configurations/exports");

        var result = await http.StartConfigurationExport(request, new ViewExportCreate { Format = ViewArchiveFormat.Git }, "acme", "web", "default");

        var accepted = result.ShouldBeOfType<AcceptedResult>();
        var export = accepted.Value.ShouldBeOfType<ViewExport>();
        accepted.Location.ShouldBe($"https://host/api/v1/acme/web/default/configurations/exports/{export.Id}");
        _transfers.Calls.ShouldBe([$"export acme/web/default Git all user_01"]);
    }

    [Fact]
    public async Task StartExport_WithoutABody_ExportsAFolder()
    {
        await Http().StartConfigurationExport(Request(), null, "acme", "web", "default");

        _transfers.Calls.ShouldBe(["export acme/web/default Folder all user_01"]);
    }

    [Fact]
    public async Task StartExport_RejectsAnUnknownFormat()
    {
        var result = await Http().StartConfigurationExport(Request(), new ViewExportCreate { Format = (ViewArchiveFormat)7 }, "acme", "web", "default");

        result.ShouldBeOfType<BadRequestObjectResult>();
        _transfers.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetExport_OfAnotherViewIsNotFound()
    {
        var export = await _transfers.StartExportAsync("acme", "web", "default", ViewArchiveFormat.Folder, null, AuditActor.System);

        var found = await Http().GetConfigurationExport(Request(), "acme", "web", "default", export.Id);
        found.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(export);

        var exception = await Should.ThrowAsync<NotFoundException>(() => Http().GetConfigurationExport(Request(), "acme", "web", "other", export.Id));
        exception.ErrorCode.ShouldBe(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task GetExportArchive_IsTheZipNamedByTheExport()
    {
        var export = await _transfers.StartExportAsync("acme", "web", "default", ViewArchiveFormat.Folder, null, AuditActor.System);

        var result = await Http().GetConfigurationExportArchive(Request(), "acme", "web", "default", export.Id);

        var file = result.ShouldBeOfType<FileStreamResult>();
        file.ContentType.ShouldBe("application/zip");
        file.FileDownloadName.ShouldBe(export.FileName);
        file.FileStream.Length.ShouldBe(3);
    }

    [Fact]
    public async Task StartImport_PassesTheArchiveAndTheOptions()
    {
        var zip = EmptyZip();
        var request = Request(zip, "https://host/api/v1/acme/web/default/configurations/imports");

        var result = await Http().StartConfigurationImport(request, "acme", "web", "default", force: true, dryRun: true, fileName: "configs.zip");

        var accepted = result.ShouldBeOfType<AcceptedResult>();
        var import = accepted.Value.ShouldBeOfType<ViewImport>();
        accepted.Location.ShouldBe($"https://host/api/v1/acme/web/default/configurations/imports/{import.Id}");
        _transfers.Calls.ShouldBe([$"import acme/web/default {zip.Length} bytes configs.zip all Overwrite force dry-run user_01"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a zip")]
    [InlineData("PK no zip")]
    public async Task StartImport_RejectsABodyThatIsNotAZip(string body)
    {
        var result = await Http().StartConfigurationImport(Request(System.Text.Encoding.UTF8.GetBytes(body)), "acme", "web", "default");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.ErrorCode.ShouldBe(ErrorCodes.InvalidArchive);
        _transfers.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task StartImport_RejectsATooLargeBody()
    {
        var http = Http(new ViewTransferOptions { MaxArchiveBytes = 2048 });
        var result = await http.StartConfigurationImport(Request(new byte[4096]), "acme", "web", "default");

        var error = result.ShouldBeOfType<ObjectResult>();
        error.StatusCode.ShouldBe((int)HttpStatusCode.RequestEntityTooLarge);
        error.Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.ArchiveTooLarge);
        _transfers.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task StartImport_RejectsATooLargeDeclaredLength()
    {
        var http = Http(new ViewTransferOptions { MaxArchiveBytes = 2048 });
        var request = Request(EmptyZip(), headers: new Dictionary<string, string> { ["Content-Length"] = "999999" });

        var result = await http.StartConfigurationImport(request, "acme", "web", "default");

        result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe((int)HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task GetImport_IsNotFoundWhenItExpired()
    {
        await Should.ThrowAsync<NotFoundException>(() => Http().GetConfigurationImport(Request(), "acme", "web", "default", "gone"));
    }

    [Fact]
    public async Task QueueFunction_ProcessesAValidMessage()
    {
        var functions = new ConfigurationTransferFunctions(_transfers, NullLogger<ConfigurationTransferFunctions>.Instance);
        var message = new ViewTransferMessage("acme", "web", "default", ViewTransferKind.Import, "0123");

        await functions.ProcessConfigurationTransfer(message.ToJson(), CancellationToken.None);

        _transfers.Calls.ShouldBe(["process Import 0123"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"Organization\":\"acme\"}")]
    public async Task QueueFunction_DropsAnInvalidMessage(string message)
    {
        var functions = new ConfigurationTransferFunctions(_transfers, NullLogger<ConfigurationTransferFunctions>.Instance);

        await functions.ProcessConfigurationTransfer(message, CancellationToken.None);

        _transfers.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task TimerFunction_Purges()
    {
        var functions = new ConfigurationTransferFunctions(_transfers, NullLogger<ConfigurationTransferFunctions>.Instance);

        await functions.PurgeConfigurationTransfers(null!, CancellationToken.None);

        _transfers.Calls.ShouldBe(["purge"]);
    }

    private static byte[] EmptyZip()
    {
        using var stream = new MemoryStream();
        using (new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
        }

        return stream.ToArray();
    }

    private static TestHttpRequestData Request(byte[]? body = null, string? url = null, Dictionary<string, string>? headers = null)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestFunctionContext(services);
        var request = new TestHttpRequestData(context, headers, "POST", bodyBytes: body, url: url is null ? null : new Uri(url));
        ((TestInvocationFeatures)context.Features).Set<IHttpRequestDataFeature>(new TestHttpRequestFeature(request));
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim> { new("sub", "user_01"), new("scp", "Configuration.ReadWrite") };
        return request;
    }

    private ConfigurationTransferHttp Http(ViewTransferOptions? options = null)
    {
        return new ConfigurationTransferHttp(_transfers, OwnerAccess.Create(), Options.Create(options ?? new ViewTransferOptions()));
    }

    private sealed class RecordingTransfers : IViewTransferService
    {
        private readonly Dictionary<string, ViewExport> _exports = [];

        public List<string> Calls { get; } = [];

        public Task<ViewExport> StartExportAsync(string organization, string project, string view, ViewArchiveFormat format, IReadOnlyList<ViewArchiveContent>? include, AuditActor actor, CancellationToken cancellationToken = default)
        {
            Calls.Add($"export {organization}/{project}/{view} {format} {(include is null ? "all" : string.Join(',', include))} {actor.Id}");
            var id = Guid.NewGuid().ToString("N");
            var export = new ViewExport
            {
                Id = id,
                Status = ViewTransferStatus.Succeeded,
                Format = format,
                FileName = ViewArchiveNames.GetArchiveFileName(organization, project, view, format, Now),
                CreatedAt = Now,
                CreatedBy = actor,
                ExpiresAt = Now.AddHours(24),
            };
            _exports[$"{organization}/{project}/{view}/{id}"] = export;
            return Task.FromResult(export);
        }

        public Task<ViewExport?> GetExportAsync(string organization, string project, string view, string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_exports.GetValueOrDefault($"{organization}/{project}/{view}/{id}"));
        }

        public async Task<(ViewExport Export, Stream Archive)> OpenExportArchiveAsync(string organization, string project, string view, string id, CancellationToken cancellationToken = default)
        {
            var export = await GetExportAsync(organization, project, view, id, cancellationToken) ?? throw new NotFoundException("missing");
            return (export, new MemoryStream([1, 2, 3]));
        }

        public Task<ViewImport> StartImportAsync(string organization, string project, string view, Stream archive, string? fileName, IReadOnlyList<ViewArchiveContent>? include, ViewImportExisting existing, bool force, bool dryRun, AuditActor actor, CancellationToken cancellationToken = default)
        {
            Calls.Add($"import {organization}/{project}/{view} {archive.Length - archive.Position} bytes {fileName} {(include is null ? "all" : string.Join(',', include))} {existing} {(force ? "force" : "-")} {(dryRun ? "dry-run" : "-")} {actor.Id}");
            return Task.FromResult(new ViewImport
            {
                Id = Guid.NewGuid().ToString("N"),
                Status = ViewTransferStatus.Queued,
                FileName = fileName,
                IsDryRun = dryRun,
                IsForced = force,
                CreatedAt = Now,
                CreatedBy = actor,
                ExpiresAt = Now.AddHours(24),
            });
        }

        public Task<ViewImport?> GetImportAsync(string organization, string project, string view, string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ViewImport?>(null);
        }

        public Task ProcessAsync(ViewTransferMessage message, CancellationToken cancellationToken = default)
        {
            Calls.Add($"process {message.Kind} {message.Id}");
            return Task.CompletedTask;
        }

        public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("purge");
            return Task.FromResult(0);
        }
    }

    // The caller is an Owner of every project; the role checks themselves are the shared helpers'.
    public class OwnerAccess : DispatchProxy
    {
        public static IAccessService Create() => Create<IAccessService, OwnerAccess>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name == nameof(IAccessService.GetAccountRoleAsync)
                ? Task.FromResult(AccountRole.Owner)
                : throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
