using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Web;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.Transfers;
using _42.Platform.Storyteller.Api.V1;
using _42.Platform.Storyteller.Transferring;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The routes of the export and import of a view: the options and their defaults, the role, the scopes per included kind
// (on requests and on reading jobs), and the background functions. Users need the Administrator role on every route
// (424-66) and no scope: the access double gives an Owner unless a test names the role, user tokens carry only
// User.Impersonation, and as Debug builds skip the role checks of users (DEV_AUTH), the tests of a refused role return
// early there. Machines have no role: every route needs Configuration.Transfer first, then the scopes of every included
// kind, each stored on the machine and carried by its credentials, in every build.
public class ViewTransferHttpTests
{
    // The scopes of the four kinds in both directions, as the credentials of a machine carry them: the read and write
    // scopes, and the management scopes an import of annotations, schemas and templates needs on top.
    private const string KindScopes = $"{Scopes.Annotation.Read} {Scopes.Annotation.Write} {Scopes.Configuration.Read} {Scopes.Configuration.Write} {ImportManagementScopes}";

    private const string ImportManagementScopes = $"{Scopes.Annotation.Manage} {Scopes.Schema.Write} {Scopes.Template.Write}";

    private const string CarriedScopes = $"{KindScopes} {Scopes.Secrets.Read} {Scopes.Secrets.Write}";

    private static readonly DateTimeOffset Now = new(2026, 10, 16, 1, 30, 12, TimeSpan.Zero);

    // Configuration.Transfer and the management scopes of the import, as a machine that imports every kind is created.
    private static readonly MachineScope[] TransferManagementScopes = [MachineScope.ConfigurationTransfer, MachineScope.AnnotationManage, MachineScope.SchemaWrite, MachineScope.TemplateWrite];

    private static readonly string EveryScope = string.Join(' ', MachineScopes.ToScopeStrings(MachineScopes.All));

    private readonly RecordingTransfers _transfers = new();

    private static bool DevAuth => Security.HttpRequestDataExtensions.IsDevelopmentAuthentication;

    [Fact]
    public async Task StartExport_AnswersAcceptedWithTheJobLocation()
    {
        var request = Request("""{ "Format": "Git" }""", url: "https://host/api/v1/acme/web/default/exports");

        var result = await Http().StartViewExport(request, "acme", "web", "default");

        var accepted = result.ShouldBeOfType<AcceptedResult>();
        var export = accepted.Value.ShouldBeOfType<ViewExport>();
        accepted.Location.ShouldBe($"https://host/api/v1/acme/web/default/exports/{export.Id}");
        export.Include.ShouldBe(ViewArchiveInclude.All);
        _transfers.Calls.ShouldBe(["export acme/web/default Git all user_01"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("{}")]
    [InlineData("""{ "Format": null, "Include": null }""")]
    [InlineData("""{ "Include": [] }""")]
    public async Task StartExport_ByDefault_ExportsAFolderOfAllFourKinds(string body)
    {
        await Http().StartViewExport(Request(body), "acme", "web", "default");

        _transfers.Calls.ShouldBe(["export acme/web/default Folder all user_01"]);
    }

    [Fact]
    public async Task StartExport_PassesTheIncludedKinds_NamedInAnyCase()
    {
        var request = Request("""{ "format": "git", "include": [ "templates", "ANNOTATIONS", "Templates" ], "Unknown": 1 }""");

        var result = await Http().StartViewExport(request, "acme", "web", "default");

        _transfers.Calls.ShouldBe(["export acme/web/default Git Templates,Annotations,Templates user_01"]);
        result.ShouldBeOfType<AcceptedResult>().Value.ShouldBeOfType<ViewExport>().Include.ShouldBe([ViewArchiveContent.Annotations, ViewArchiveContent.Templates], "the job stores the include resolved");
    }

    [Theory]
    [InlineData("""{ "Format": "Zip" }""", "Unknown archive format \"Zip\". Use Folder or Git.")]
    [InlineData("""{ "Format": 1 }""", "Unknown archive format 1. Use Folder or Git.")]
    [InlineData("""{ "Include": [ "Annotations", "Secrets" ] }""", "Include has the unknown kind \"Secrets\". Use Annotations, Schemas, Templates or Configurations, or an empty list for all four.")]
    [InlineData("""{ "Include": [ 0 ] }""", "Include has the unknown kind 0. Use Annotations, Schemas, Templates or Configurations, or an empty list for all four.")]
    [InlineData("""{ "Include": "Annotations" }""", "Include must be a list of kinds: Annotations, Schemas, Templates or Configurations.")]
    [InlineData("""[ "Annotations" ]""", "The body must be a JSON object: { \"Format\": \"Folder\" | \"Git\", \"Include\": [ ... ] }.")]
    public async Task StartExport_RejectsAnUnknownFormatOrKind(string body, string message)
    {
        var result = await Http().StartViewExport(Request(body), "acme", "web", "default");

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().Message.ShouldBe(message);
        _transfers.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task StartExport_RejectsABodyThatIsNotJson()
    {
        var result = await Http().StartViewExport(Request("{ \"Format\": "), "acme", "web", "default");

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().Message.ShouldStartWith("The body is not valid JSON:");
        _transfers.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetExport_OfAnotherViewIsNotFound()
    {
        var export = await _transfers.StartExportAsync("acme", "web", "default", ViewArchiveFormat.Folder, null, AuditActor.System);
        _transfers.Calls.Clear();

        var found = await Http().GetViewExport(Request(), "acme", "web", "default", export.Id);
        found.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(export);

        var exception = await Should.ThrowAsync<NotFoundException>(() => Http().GetViewExport(Request(), "acme", "web", "other", export.Id));
        exception.ErrorCode.ShouldBe(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task GetExportArchive_IsTheZipNamedByTheExport()
    {
        var export = await _transfers.StartExportAsync("acme", "web", "default", ViewArchiveFormat.Folder, null, AuditActor.System);

        var result = await Http().GetViewExportArchive(Request(), "acme", "web", "default", export.Id);

        var file = result.ShouldBeOfType<FileStreamResult>();
        file.ContentType.ShouldBe("application/zip");
        file.FileDownloadName.ShouldBe(export.FileName);
        file.FileStream.Length.ShouldBe(3);
    }

    [Fact]
    public async Task GetExportArchive_OfAnUnknownExportIsNotFound()
    {
        await Should.ThrowAsync<NotFoundException>(() => Http().GetViewExportArchive(Request(), "acme", "web", "default", "gone"));

        _transfers.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task StartImport_PassesTheArchiveAndTheOptions()
    {
        var zip = EmptyZip();
        var request = Request(zip, "https://host/api/v1/acme/web/default/imports", query: "include=annotations,%20Templates&existing=skip");

        var result = await Http().StartViewImport(request, "acme", "web", "default", force: true, dryRun: true, fileName: "view.zip");

        var accepted = result.ShouldBeOfType<AcceptedResult>();
        var import = accepted.Value.ShouldBeOfType<ViewImport>();
        accepted.Location.ShouldBe($"https://host/api/v1/acme/web/default/imports/{import.Id}");
        import.Include.ShouldBe([ViewArchiveContent.Annotations, ViewArchiveContent.Templates]);
        import.Existing.ShouldBe(ViewImportExisting.Skip);
        _transfers.Calls.ShouldBe([$"import acme/web/default {zip.Length} bytes view.zip Annotations,Templates Skip force dry-run user_01"]);
    }

    [Fact]
    public async Task StartImport_ByDefault_ImportsAllFourKindsAndOverwrites()
    {
        var zip = EmptyZip();

        var result = await Http().StartViewImport(Request(zip), "acme", "web", "default");

        result.ShouldBeOfType<AcceptedResult>().Value.ShouldBeOfType<ViewImport>().Include.ShouldBe(ViewArchiveInclude.All);
        _transfers.Calls.ShouldBe([$"import acme/web/default {zip.Length} bytes  all Overwrite - - user_01"]);
    }

    [Theory]
    [InlineData("include=&existing=", "all Overwrite")]
    [InlineData("include=%20,%20", "all Overwrite")]
    [InlineData("include=CONFIGURATIONS&existing=OVERWRITE", "Configurations Overwrite")]
    [InlineData("include=schemas,configurations,schemas&existing=Skip", "Schemas,Configurations,Schemas Skip")]
    [InlineData("include=Annotations,Schemas,Templates,Configurations&existing=%20skip%20", "Annotations,Schemas,Templates,Configurations Skip")]
    [InlineData("include=annotations&include=templates", "Annotations,Templates Overwrite")]
    public async Task StartImport_ParsesIncludeAndExisting_InAnyCase(string query, string expected)
    {
        var zip = EmptyZip();

        await Http().StartViewImport(Request(zip, query: query), "acme", "web", "default");

        _transfers.Calls.ShouldBe([$"import acme/web/default {zip.Length} bytes  {expected} - - user_01"]);
    }

    [Theory]
    [InlineData("annotations,secrets", "'secrets'")]
    [InlineData("annotation", "'annotation'")]
    [InlineData("1", "'1'")]
    [InlineData("Annotations|Schemas", "'Annotations|Schemas'")]
    public async Task StartImport_RejectsAnUnknownKind(string include, string named)
    {
        var exception = await Should.ThrowAsync<InvalidInputException>(() => Http().StartViewImport(Request(EmptyZip(), query: $"include={Uri.EscapeDataString(include)}"), "acme", "web", "default"));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidQuery);
        exception.Message.ShouldBe($"The query parameter 'include' has the unknown kind {named}. Use Annotations, Schemas, Templates or Configurations, comma-separated, or leave it empty for all four.");
        _transfers.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("merge")]
    [InlineData("1")]
    [InlineData("Overwrite,Skip")]
    public async Task StartImport_RejectsAnUnknownExistingValue(string existing)
    {
        var exception = await Should.ThrowAsync<InvalidInputException>(() => Http().StartViewImport(Request(EmptyZip(), query: $"existing={Uri.EscapeDataString(existing)}"), "acme", "web", "default"));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidQuery);
        exception.Message.ShouldBe($"The query parameter 'existing' must be Overwrite or Skip, not '{existing}'.");
        _transfers.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a zip")]
    [InlineData("PK no zip")]
    public async Task StartImport_RejectsABodyThatIsNotAZip(string body)
    {
        var result = await Http().StartViewImport(Request(Encoding.UTF8.GetBytes(body)), "acme", "web", "default");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.ErrorCode.ShouldBe(ErrorCodes.InvalidArchive);
        _transfers.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task StartImport_RejectsATooLargeBody()
    {
        var http = Http(new ViewTransferOptions { MaxArchiveBytes = 2048 });
        var result = await http.StartViewImport(Request(new byte[4096]), "acme", "web", "default");

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

        var result = await http.StartViewImport(request, "acme", "web", "default");

        result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe((int)HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task GetImport_IsNotFoundWhenItExpired()
    {
        await Should.ThrowAsync<NotFoundException>(() => Http().GetViewImport(Request(), "acme", "web", "default", "gone"));
    }

    [Fact]
    public async Task GetImport_AnswersTheStoredJob()
    {
        var import = _transfers.AddImport("acme", "web", "default", [ViewArchiveContent.Configurations]);

        var result = await Http().GetViewImport(Request(), "acme", "web", "default", import.Id);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(import);
    }

    // Scopes per kind: the rule of each kind, for an export and reading it, and for an import and reading it. An import
    // needs the scopes of the direct writes of the kind, its management scope too.
    [Theory]
    [InlineData(ViewArchiveContent.Annotations, ViewTransferKind.Export, new[] { MachineScope.AnnotationRead })]
    [InlineData(ViewArchiveContent.Schemas, ViewTransferKind.Export, new[] { MachineScope.ConfigurationRead })]
    [InlineData(ViewArchiveContent.Templates, ViewTransferKind.Export, new[] { MachineScope.ConfigurationRead })]
    [InlineData(ViewArchiveContent.Configurations, ViewTransferKind.Export, new[] { MachineScope.ConfigurationRead })]
    [InlineData(ViewArchiveContent.Annotations, ViewTransferKind.Import, new[] { MachineScope.AnnotationWrite, MachineScope.AnnotationManage })]
    [InlineData(ViewArchiveContent.Schemas, ViewTransferKind.Import, new[] { MachineScope.ConfigurationWrite, MachineScope.SchemaWrite })]
    [InlineData(ViewArchiveContent.Templates, ViewTransferKind.Import, new[] { MachineScope.ConfigurationWrite, MachineScope.TemplateWrite })]
    [InlineData(ViewArchiveContent.Configurations, ViewTransferKind.Import, new[] { MachineScope.ConfigurationWrite })]
    public void Scopes_OfEachKind(ViewArchiveContent content, ViewTransferKind kind, MachineScope[] expected)
    {
        ViewTransferScopes.GetScopes(content, kind).ShouldBe(expected);
    }

    // A machine exports a kind with its read scope: Annotation.Read for annotations, Configuration.Read for schemas,
    // templates and configurations. A write scope, a secrets scope or User.Impersonation does not stand in for it, the
    // scope strings must match exactly, and the strings of the removed presets are unknown. Every row runs for a machine
    // whose credentials carry the scopes and for one created with them.
    [Theory]
    [InlineData("Annotation.Read Configuration.Read", null, true)]
    [InlineData("Configuration.Read", null, false)]
    [InlineData("Annotation.Write Configuration.Write", null, false)]
    [InlineData("Configuration.Read", "Schemas,Templates,Configurations", true)]
    [InlineData("Configuration.Read", "Configurations", true)]
    [InlineData("Configuration.Write", "Configurations", false)]
    [InlineData("Configuration.Read", "Annotations", false)]
    [InlineData("Annotation.Read", "Annotations", true)]
    [InlineData("Annotation.Write", "Annotations", false)]
    [InlineData("Annotation.Read", "Annotations,Templates", false)]
    [InlineData("Secrets.Read", "Configurations", false)]
    [InlineData("User.Impersonation", "Configurations", false)]
    [InlineData("annotation.read configuration.read", null, false)]
    [InlineData("Default.Read", null, false)]
    [InlineData("Annotation.ReadWrite Configuration.ReadWrite", null, false)]
    [InlineData("", "Schemas", false)]
    public async Task StartExport_ByAMachine_NeedsTheReadScopeOfEveryIncludedKind(string scopes, string? include, bool isAllowed)
    {
        var body = include is null ? string.Empty : $$"""{ "Include": [ {{string.Join(", ", include.Split(',').Select(kind => $"\"{kind}\""))}} ] }""";

        await ShouldFollowTheKindScopes(scopes, isAllowed, (http, carried) => http.StartViewExport(Request(body, scopes: carried, machine: true), "acme", "web", "default"));
    }

    // A machine imports a kind with the scopes of its direct writes: Annotation.Write and Annotation.Manage for
    // annotations, Configuration.Write and Schema.Write for schemas, Configuration.Write and Template.Write for templates,
    // Configuration.Write for configurations. The write scope without the management scope of its kind is not enough,
    // and another management scope does not stand in for it. Every row runs for a machine whose credentials carry the
    // scopes and for one created with them.
    [Theory]
    [InlineData("Annotation.Write Annotation.Manage Configuration.Write Schema.Write Template.Write", null, true)]
    [InlineData("Annotation.Write Configuration.Write", null, false)]
    [InlineData("Annotation.Read Configuration.Read", null, false)]
    [InlineData("Configuration.Write", null, false)]
    [InlineData("Configuration.Write Schema.Write Template.Write", "schemas,templates,configurations", true)]
    [InlineData("Configuration.Write", "schemas,templates,configurations", false)]
    [InlineData("Configuration.Write", "configurations", true)]
    [InlineData("Configuration.Read", "configurations", false)]
    [InlineData("Configuration.Write", "annotations", false)]
    [InlineData("Annotation.Write Annotation.Manage", "annotations", true)]
    [InlineData("Annotation.Write", "annotations", false)]
    [InlineData("Annotation.Manage", "annotations", false)]
    [InlineData("Configuration.Write Schema.Write", "schemas", true)]
    [InlineData("Configuration.Write", "schemas", false)]
    [InlineData("Schema.Write", "schemas", false)]
    [InlineData("Configuration.Write Template.Write", "templates", true)]
    [InlineData("Configuration.Write", "templates", false)]
    [InlineData("Configuration.Write Schema.Write", "templates", false)]
    [InlineData("Configuration.Write KeyValue.Manage", "schemas,templates", false)]
    [InlineData("Annotation.Read Configuration.Write", "annotations,configurations", false)]
    [InlineData("Annotation.Write Annotation.Manage", "annotations,templates", false)]
    [InlineData("Secrets.Write", "configurations", false)]
    [InlineData("Default.ReadWrite", null, false)]
    [InlineData("Annotation.ReadWrite", "annotations", false)]
    public async Task StartImport_ByAMachine_NeedsTheWriteScopesOfEveryIncludedKind(string scopes, string? include, bool isAllowed)
    {
        var query = include is null ? null : $"include={include}";

        await ShouldFollowTheKindScopes(scopes, isAllowed, (http, carried) => http.StartViewImport(Request(EmptyZip(), scopes: carried, machine: true, query: query), "acme", "web", "default"));
    }

    // Users need no scope of an included kind: the Administrator role decides, and their tokens carry only
    // User.Impersonation.
    [Theory]
    [InlineData(null)]
    [InlineData(new[] { ViewArchiveContent.Annotations })]
    [InlineData(new[] { ViewArchiveContent.Schemas, ViewArchiveContent.Templates, ViewArchiveContent.Configurations })]
    public async Task EveryRoute_ByAnAdministrator_NeedsNoScopeOfAnIncludedKind(ViewArchiveContent[]? include)
    {
        var http = Http(role: AccountRole.Administrator);
        var kinds = include?.Select(kind => kind.ToString()).ToList() ?? [];
        var body = $$"""{ "Include": [ {{string.Join(", ", kinds.Select(kind => $"\"{kind}\""))}} ] }""";

        var export = (await http.StartViewExport(Request(body), "acme", "web", "default"))
            .ShouldBeOfType<AcceptedResult>().Value.ShouldBeOfType<ViewExport>();
        (await http.GetViewExport(Request(), "acme", "web", "default", export.Id)).ShouldBeOfType<OkObjectResult>();
        (await http.GetViewExportArchive(Request(), "acme", "web", "default", export.Id)).ShouldBeOfType<FileStreamResult>();
        var import = (await http.StartViewImport(Request(EmptyZip(), query: kinds.Count == 0 ? null : $"include={string.Join(',', kinds)}"), "acme", "web", "default"))
            .ShouldBeOfType<AcceptedResult>().Value.ShouldBeOfType<ViewImport>();
        (await http.GetViewImport(Request(), "acme", "web", "default", import.Id)).ShouldBeOfType<OkObjectResult>();

        export.Include.ShouldBe(ViewArchiveInclude.Resolve(include));
        import.Include.ShouldBe(ViewArchiveInclude.Resolve(include));
    }

    [Fact]
    public async Task StartImport_ByAMachineWithOnlyConfigurationWrite_NamesTheMissingScopesAndInclude()
    {
        var act = () => Http().StartViewImport(Request(EmptyZip(), scopes: Scopes.Configuration.Write, machine: true), "acme", "web", "default");

        var exception = await act.ShouldThrowAsync<AccessDeniedException>();
        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("Missing scope for annotations: importing them needs Annotation.Write and Annotation.Manage. Missing scope for schemas: importing them needs Configuration.Write and Schema.Write. Missing scope for templates: importing them needs Configuration.Write and Template.Write. Without include, all four kinds are included; name the kinds to import in include.");
        _transfers.Calls.ShouldBeEmpty();

        await Http().StartViewImport(Request(EmptyZip(), scopes: Scopes.Configuration.Write, machine: true, query: "include=configurations"), "acme", "web", "default");
        _transfers.Calls.ShouldHaveSingleItem().ShouldContain("Configurations Overwrite");
    }

    // A sentence names every scope the kind needs, also those the machine has, so it does not change with the machine.
    [Theory]
    [InlineData("Configuration.Write", "schemas", "Missing scope for schemas in include: importing them needs Configuration.Write and Schema.Write.")]
    [InlineData("Schema.Write", "schemas", "Missing scope for schemas in include: importing them needs Configuration.Write and Schema.Write.")]
    [InlineData("Configuration.Write", "templates,configurations", "Missing scope for templates in include: importing them needs Configuration.Write and Template.Write.")]
    [InlineData("Annotation.Write", "annotations", "Missing scope for annotations in include: importing them needs Annotation.Write and Annotation.Manage.")]
    [InlineData("Annotation.Write Configuration.Write", "annotations,schemas,templates", "Missing scope for annotations in include: importing them needs Annotation.Write and Annotation.Manage. Missing scope for schemas in include: importing them needs Configuration.Write and Schema.Write. Missing scope for templates in include: importing them needs Configuration.Write and Template.Write.")]
    public async Task StartImport_ByAMachineWithoutTheManagementScopeOfAnIncludedKind_NamesEveryScopeOfTheKind(string scopes, string include, string message)
    {
        var act = () => Http(managementScopes: TransferManagementScopes).StartViewImport(Request(EmptyZip(), scopes: scopes, machine: true, query: $"include={include}"), "acme", "web", "default");

        var exception = await act.ShouldThrowAsync<AccessDeniedException>();
        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe(message);
        _transfers.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Start_ByAMachineRestrictedToAnAnnotation_IsRefused()
    {
        var http = Http(machineAnnotationKey: "rst.payments");

        // an export or an import spans the whole view, so a machine restricted to one annotation reaches neither
        var export = await Should.ThrowAsync<AccessDeniedException>(() => http.StartViewExport(Request(Encoding.UTF8.GetBytes("{}"), machine: true), "acme", "web", "default"));
        var import = await Should.ThrowAsync<AccessDeniedException>(() => http.StartViewImport(Request(EmptyZip(), machine: true), "acme", "web", "default"));

        export.ErrorCode.ShouldBe(ErrorCodes.AnnotationRestricted);
        import.ErrorCode.ShouldBe(ErrorCodes.AnnotationRestricted);
        _transfers.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task StartExport_ByAMachineWithAnIncludedKindWithoutItsScope_NamesTheScopeAndInclude()
    {
        var act = () => Http().StartViewExport(Request("""{ "Include": [ "Annotations", "Templates" ] }""", scopes: Scopes.Annotation.Read, machine: true), "acme", "web", "default");

        var exception = await act.ShouldThrowAsync<AccessDeniedException>();
        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("Missing scope for templates in include: exporting them needs Configuration.Read.");
        _transfers.Calls.ShouldBeEmpty();
    }

    // Kinds that need the same scopes share their sentence. The secrets scopes and the other management scopes stand in
    // for no kind, and the management scopes of the import without the write scopes leave the message as it is.
    [Theory]
    [InlineData(ViewTransferKind.Export, "Missing scope for annotations: exporting them needs Annotation.Read. Missing scope for schemas, templates and configurations: exporting them needs Configuration.Read. Without include, all four kinds are included; name the kinds to export in include.")]
    [InlineData(ViewTransferKind.Import, "Missing scope for annotations: importing them needs Annotation.Write and Annotation.Manage. Missing scope for schemas: importing them needs Configuration.Write and Schema.Write. Missing scope for templates: importing them needs Configuration.Write and Template.Write. Missing scope for configurations: importing them needs Configuration.Write. Without include, all four kinds are included; name the kinds to import in include.")]
    public void Scopes_WithoutAnyKind_NameEveryMissingRule(ViewTransferKind kind, string message)
    {
        var others = MachineScopes.All.Where(scope => scope is not (MachineScope.AnnotationRead or MachineScope.AnnotationWrite or MachineScope.ConfigurationRead or MachineScope.ConfigurationWrite)).ToHashSet();

        Should.Throw<AccessDeniedException>(() => ViewTransferScopes.Ensure(new HashSet<MachineScope>(), null, kind)).Message.ShouldBe(message);
        Should.Throw<AccessDeniedException>(() => ViewTransferScopes.Ensure(others, null, kind)).Message.ShouldBe(message);
    }

    // Reading a job checks the read scopes of the include stored with it, not those of the request. Every row runs for
    // a machine whose credentials carry the scopes and for one created with them.
    [Theory]
    [InlineData("Configuration.Read", new[] { ViewArchiveContent.Configurations }, true)]
    [InlineData("Configuration.Read", new[] { ViewArchiveContent.Annotations, ViewArchiveContent.Configurations }, false)]
    [InlineData("Annotation.Read", new[] { ViewArchiveContent.Annotations }, true)]
    [InlineData("Annotation.Write", new[] { ViewArchiveContent.Annotations }, false)]
    [InlineData("Annotation.Read Configuration.Read", new[] { ViewArchiveContent.Annotations, ViewArchiveContent.Schemas, ViewArchiveContent.Templates, ViewArchiveContent.Configurations }, true)]
    [InlineData("Default.Read", new[] { ViewArchiveContent.Annotations, ViewArchiveContent.Schemas, ViewArchiveContent.Templates, ViewArchiveContent.Configurations }, false)]
    public async Task GetExport_ByAMachine_NeedsTheReadScopesOfTheKindsTheExportIncludes(string scopes, ViewArchiveContent[] include, bool isAllowed)
    {
        var export = await _transfers.StartExportAsync("acme", "web", "default", ViewArchiveFormat.Folder, include, AuditActor.System);
        _transfers.Calls.Clear();

        await ShouldFollowTheKindScopes(scopes, isAllowed, (http, carried) => http.GetViewExport(Request(scopes: carried, machine: true), "acme", "web", "default", export.Id));
        await ShouldFollowTheKindScopes(scopes, isAllowed, (http, carried) => http.GetViewExportArchive(Request(scopes: carried, machine: true), "acme", "web", "default", export.Id));

        // opened once for the carried scopes and once for the stored ones
        string[] expected = isAllowed ? [$"open {export.Id}", $"open {export.Id}"] : [];
        _transfers.Calls.ShouldBe(expected, ignoreOrder: false, "a denied download never opens the archive");
    }

    [Fact]
    public async Task GetExport_ByAMachineWithoutTheScopeOfAnIncludedKind_NamesItAndTheJobsInclude()
    {
        var export = await _transfers.StartExportAsync("acme", "web", "default", ViewArchiveFormat.Git, [ViewArchiveContent.Annotations], AuditActor.System);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => Http().GetViewExport(Request(scopes: Scopes.Configuration.Read, machine: true), "acme", "web", "default", export.Id));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("Missing scope for annotations in the include of this export: reading it needs Annotation.Read.");
    }

    // And reading an import the scopes of importing its kinds, the management scopes too.
    [Theory]
    [InlineData("Configuration.Write Schema.Write", new[] { ViewArchiveContent.Schemas, ViewArchiveContent.Configurations }, true)]
    [InlineData("Configuration.Write", new[] { ViewArchiveContent.Schemas, ViewArchiveContent.Configurations }, false)]
    [InlineData("Configuration.Write", new[] { ViewArchiveContent.Configurations }, true)]
    [InlineData("Configuration.Write", new[] { ViewArchiveContent.Annotations, ViewArchiveContent.Configurations }, false)]
    [InlineData("Configuration.Read", new[] { ViewArchiveContent.Configurations }, false)]
    [InlineData("Annotation.Write Annotation.Manage", new[] { ViewArchiveContent.Annotations }, true)]
    [InlineData("Annotation.Write", new[] { ViewArchiveContent.Annotations }, false)]
    [InlineData("Configuration.Write Template.Write", new[] { ViewArchiveContent.Templates }, true)]
    [InlineData("Configuration.Write", new[] { ViewArchiveContent.Templates }, false)]
    [InlineData("Annotation.Write Annotation.Manage Configuration.Write Schema.Write Template.Write", new[] { ViewArchiveContent.Annotations, ViewArchiveContent.Schemas, ViewArchiveContent.Templates, ViewArchiveContent.Configurations }, true)]
    [InlineData("Annotation.Write Configuration.Write", new[] { ViewArchiveContent.Annotations, ViewArchiveContent.Schemas, ViewArchiveContent.Templates, ViewArchiveContent.Configurations }, false)]
    [InlineData("Default.ReadWrite", new[] { ViewArchiveContent.Annotations, ViewArchiveContent.Schemas, ViewArchiveContent.Templates, ViewArchiveContent.Configurations }, false)]
    public async Task GetImport_ByAMachine_NeedsTheImportScopesOfTheKindsTheImportIncludes(string scopes, ViewArchiveContent[] include, bool isAllowed)
    {
        var import = _transfers.AddImport("acme", "web", "default", include);

        await ShouldFollowTheKindScopes(scopes, isAllowed, (http, carried) => http.GetViewImport(Request(scopes: carried, machine: true), "acme", "web", "default", import.Id));
    }

    [Fact]
    public async Task GetImport_ByAMachine_OfAJobWithoutInclude_NeedsTheImportScopesOfAllFourKinds()
    {
        var import = _transfers.AddImport("acme", "web", "default", null);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => Http().GetViewImport(Request(scopes: Scopes.Configuration.Write, machine: true), "acme", "web", "default", import.Id));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("Missing scope for annotations in the include of this import: reading it needs Annotation.Write and Annotation.Manage. Missing scope for schemas in the include of this import: reading it needs Configuration.Write and Schema.Write. Missing scope for templates in the include of this import: reading it needs Configuration.Write and Template.Write.");
    }

    // Without credentials there are no scopes to check: 401 (SecurityTokenException), as on every route and as in the
    // first spec, never a 403 that asks the caller to change include. Clients sign in again only on a 401.
    [Theory]
    [InlineData(null)]
    [InlineData("""{ "Include": [ "Configurations" ] }""")]
    public async Task StartExport_WithoutCredentials_IsUnauthorized(string? body)
    {
        var request = body is null ? Request(isAnonymous: true) : Request(body, isAnonymous: true);

        await Should.ThrowAsync<SecurityTokenException>(() => Http().StartViewExport(request, "acme", "web", "default"));

        _transfers.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("include=configurations")]
    public async Task StartImport_WithoutCredentials_IsUnauthorized(string? query)
    {
        var request = Request(EmptyZip(), query: query, isAnonymous: true);

        await Should.ThrowAsync<SecurityTokenException>(() => Http().StartViewImport(request, "acme", "web", "default"));

        _transfers.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetJobs_WithoutCredentials_AreUnauthorized()
    {
        var export = await _transfers.StartExportAsync("acme", "web", "default", ViewArchiveFormat.Folder, [ViewArchiveContent.Configurations], AuditActor.System);
        var import = _transfers.AddImport("acme", "web", "default", [ViewArchiveContent.Configurations]);
        _transfers.Calls.Clear();
        var http = Http();

        await Should.ThrowAsync<SecurityTokenException>(() => http.GetViewExport(Request(isAnonymous: true), "acme", "web", "default", export.Id));
        await Should.ThrowAsync<SecurityTokenException>(() => http.GetViewExportArchive(Request(isAnonymous: true), "acme", "web", "default", export.Id));
        await Should.ThrowAsync<SecurityTokenException>(() => http.GetViewImport(Request(isAnonymous: true), "acme", "web", "default", import.Id));

        _transfers.Calls.ShouldBeEmpty("an anonymous download never opens the archive");
    }

    // Users need the Administrator role on the project on every route, also to read a job (424-66); a Debug build
    // (DEV_AUTH) skips the role checks of users. Every scope in the token changes nothing, as users need none, and the
    // reads check the role before the job, so a refused user learns nothing about it.
    [Theory]
    [InlineData(AccountRole.None)]
    [InlineData(AccountRole.Reader)]
    [InlineData(AccountRole.Contributor)]
    [InlineData(AccountRole.ContributorWithSecrets)]
    public async Task EveryRoute_ByAUserBelowAdministrator_IsRefused(AccountRole role)
    {
        if (DevAuth)
        {
            return;
        }

        var export = await _transfers.StartExportAsync("acme", "web", "default", ViewArchiveFormat.Folder, null, AuditActor.System);
        var import = _transfers.AddImport("acme", "web", "default", null);
        _transfers.Calls.Clear();
        var http = Http(role: role);

        (string Route, Func<Task<IActionResult>> Act)[] routes =
        [
            (nameof(ViewTransferHttp.StartViewExport), () => http.StartViewExport(Request("{}", scopes: EveryScope), "acme", "web", "default")),
            (nameof(ViewTransferHttp.GetViewExport), () => http.GetViewExport(Request(scopes: EveryScope), "acme", "web", "default", export.Id)),
            (nameof(ViewTransferHttp.GetViewExportArchive), () => http.GetViewExportArchive(Request(scopes: EveryScope), "acme", "web", "default", export.Id)),
            (nameof(ViewTransferHttp.StartViewImport), () => http.StartViewImport(Request(EmptyZip(), scopes: EveryScope), "acme", "web", "default")),
            (nameof(ViewTransferHttp.GetViewImport), () => http.GetViewImport(Request(scopes: EveryScope), "acme", "web", "default", import.Id)),
        ];

        foreach (var (route, act) in routes)
        {
            var exception = await act.ShouldThrowAsync<AccessDeniedException>(route);
            exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied, route);
            exception.Message.ShouldBe("No Administrator access to 'acme.web'.", route);
        }

        _transfers.Calls.ShouldBeEmpty("a refused user starts no job and never opens the archive");
        _transfers.Reads.ShouldBeEmpty("a refused user never reads a job");
    }

    // Only a Release build checks the role; a Debug build passes every user. The tokens carry only User.Impersonation.
    [Theory]
    [InlineData(AccountRole.Administrator)]
    [InlineData(AccountRole.Owner)]
    public async Task EveryRoute_ByAnAdministratorOrAnOwner_Succeeds(AccountRole role)
    {
        await ShouldPassEveryRoute(Http(role: role), machine: false);
    }

    // A machine has no role, and the double has none for it either: Configuration.Transfer and the scopes of the
    // included kinds decide, the management scopes of the import among them, and the machine needs no other scope.
    [Fact]
    public async Task EveryRoute_ByAMachineWithTheKindScopesAndConfigurationTransfer_SucceedsWithoutARole()
    {
        MachineScope[] scopes = [MachineScope.AnnotationRead, MachineScope.AnnotationWrite, MachineScope.ConfigurationRead, MachineScope.ConfigurationWrite, .. TransferManagementScopes];

        await ShouldPassEveryRoute(Http(role: AccountRole.None, scopes: scopes), machine: true);
    }

    // The management scope is checked with the machine document in every build. The reads check it before the job, so a
    // refused machine learns nothing about it.
    [Fact]
    public async Task EveryRoute_ByAMachineWithTheRuntimeScopesOnly_IsAccessDenied()
    {
        await ShouldRefuseEveryRouteToAMachine(
            Http(role: AccountRole.None, managementScopes: []),
            machineManagementScopes: string.Empty,
            ErrorCodes.AccessDenied,
            "This operation needs the scope Configuration.Transfer, which the machine was not created with.");
    }

    // A federated token that asked for a subset of the machine's scopes, without Configuration.Transfer.
    [Fact]
    public async Task EveryRoute_ByAMachineWhoseCredentialsDoNotCarryConfigurationTransfer_IsAccessDenied()
    {
        await ShouldRefuseEveryRouteToAMachine(
            Http(role: AccountRole.None),
            machineManagementScopes: string.Empty,
            ErrorCodes.AccessDenied,
            "This operation needs the scope Configuration.Transfer, which the credentials of the machine do not carry.");
    }

    // Another management scope does not stand in for the transfer scope.
    [Fact]
    public async Task EveryRoute_ByAMachineWithOtherManagementScopes_IsAccessDenied()
    {
        MachineScope[] others = [MachineScope.SchemaWrite, MachineScope.TemplateWrite, MachineScope.AnnotationManage, MachineScope.KeyValueManage];

        await ShouldRefuseEveryRouteToAMachine(
            Http(role: AccountRole.None, managementScopes: others),
            machineManagementScopes: $"{Scopes.Schema.Write} {Scopes.Template.Write} {Scopes.Annotation.Manage} {Scopes.KeyValue.Manage}",
            ErrorCodes.AccessDenied,
            "This operation needs the scope Configuration.Transfer, which the machine was not created with.");
    }

    // The restriction comes first: a machine restricted to an annotation learns that a transfer is out of its reach,
    // not which scope it lacks.
    [Fact]
    public async Task EveryRoute_ByARestrictedMachineWithoutAnyScope_IsRestrictedFirst()
    {
        await ShouldRefuseEveryRouteToAMachine(
            Http(machineAnnotationKey: "rst.payments", role: AccountRole.None, scopes: []),
            machineManagementScopes: string.Empty,
            ErrorCodes.AnnotationRestricted,
            message: null,
            scopes: string.Empty);
    }

    // Configuration.Transfer comes before the kinds on every route, the starts too: the kinds are checked with the
    // machine of the access check. A machine without either learns first that it is not a transfer machine, and a read
    // never reads the job. Once for a machine created without both, once for credentials that carry neither.
    [Fact]
    public async Task EveryRoute_ByAMachineWithoutConfigurationTransferOrTheScopeOfAKind_NamesConfigurationTransferFirst()
    {
        await ShouldRefuseEveryRouteToAMachine(
            Http(role: AccountRole.None, scopes: [MachineScope.ConfigurationWrite]),
            machineManagementScopes: string.Empty,
            ErrorCodes.AccessDenied,
            "This operation needs the scope Configuration.Transfer, which the machine was not created with.",
            scopes: Scopes.Configuration.Write);

        await ShouldRefuseEveryRouteToAMachine(
            Http(role: AccountRole.None),
            machineManagementScopes: string.Empty,
            ErrorCodes.AccessDenied,
            "This operation needs the scope Configuration.Transfer, which the credentials of the machine do not carry.",
            scopes: Scopes.Configuration.Write);
    }

    // The routes of the first spec below configurations/ were never released and are gone.
    [Fact]
    public void Routes_OfTheFirstSpecAreRemoved()
    {
        var routes = typeof(AccessHttp).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>()))
            .Where(trigger => trigger?.Route is not null)
            .Select(trigger => trigger!.Route!)
            .ToList();

        routes.ShouldContain(Definitions.Routes.Transfers.V1.Exports);
        routes.ShouldNotContain(route => route.Contains("/configurations/exports", StringComparison.Ordinal) || route.Contains("/configurations/imports", StringComparison.Ordinal));
        typeof(Definitions.Routes.Configuration.V1).GetFields().Select(field => field.Name).ShouldNotContain(name => name.Contains("Export") || name.Contains("Import"));
        typeof(Definitions.RouteIds.Configuration).GetFields().Select(field => field.Name).ShouldNotContain(name => name.Contains("Export") || name.Contains("Import"));
        typeof(AccessHttp).Assembly.GetTypes().Select(type => type.Name).ShouldNotContain(name => name.StartsWith("ConfigurationTransfer", StringComparison.Ordinal));
    }

    [Fact]
    public void Routes_AreBelowTheViewWithViewLevelOperationIds()
    {
        var operations = typeof(ViewTransferHttp)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(method => (
                Function: method.GetCustomAttribute<FunctionAttribute>()!.Name,
                Operation: method.GetCustomAttribute<Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes.OpenApiOperationAttribute>()!,
                Trigger: method.GetParameters().Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>()).Single(trigger => trigger is not null)!))
            .Select(operation => $"{operation.Trigger.Methods!.Single().ToUpperInvariant()} {operation.Trigger.Route} {operation.Function} {operation.Operation.OperationId} {string.Join(',', operation.Operation.Tags)}")
            .ToList();

        operations.ShouldBe(
            [
                "POST v1/{organization}/{project}/{view}/exports StartViewExport StartViewExport Transfers",
                "GET v1/{organization}/{project}/{view}/exports/{id} GetViewExport GetViewExport Transfers",
                "GET v1/{organization}/{project}/{view}/exports/{id}/archive GetViewExportArchive GetViewExportArchive Transfers",
                "POST v1/{organization}/{project}/{view}/imports StartViewImport StartViewImport Transfers",
                "GET v1/{organization}/{project}/{view}/imports/{id} GetViewImport GetViewImport Transfers",
            ],
            ignoreOrder: true);
    }

    // The 403 of every route names the role users need and that they need no data scope (424-66), the restriction of
    // machines, and what a machine needs: Configuration.Transfer, then the read scope of every included kind to export
    // and the scopes of its direct writes to import.
    [Fact]
    public async Task Document_TheForbiddenOfEveryRoute_NamesTheAdministratorRoleAndTheScopesOfMachines()
    {
        var json = await OpenApiExportTests.RenderAsync();
        using var document = JsonDocument.Parse(json);

        var forbidden = new Dictionary<string, string>
        {
            [Definitions.RouteIds.Transfers.StartViewExport] = Definitions.Descriptions.ResponseTransferForbiddenExport,
            [Definitions.RouteIds.Transfers.GetViewExport] = Definitions.Descriptions.ResponseTransferForbiddenExport,
            [Definitions.RouteIds.Transfers.GetViewExportArchive] = Definitions.Descriptions.ResponseTransferForbiddenExport,
            [Definitions.RouteIds.Transfers.StartViewImport] = Definitions.Descriptions.ResponseTransferForbiddenImport,
            [Definitions.RouteIds.Transfers.GetViewImport] = Definitions.Descriptions.ResponseTransferForbiddenImport,
        };

        foreach (var (operationId, expected) in forbidden)
        {
            var isExport = expected == Definitions.Descriptions.ResponseTransferForbiddenExport;
            var kindScopes = isExport
                ? "the read scope of every included kind (Annotation.Read for annotations, Configuration.Read for schemas, templates and configurations); reading an export needs the scopes of the kinds it includes."
                : "the scopes of the direct writes of every included kind (Annotation.Write and Annotation.Manage for annotations, Configuration.Write and Schema.Write for schemas, Configuration.Write and Template.Write for templates and Configuration.Write for configurations); reading an import needs the scopes of the kinds it includes.";

            var description = DescriptionOf(document, operationId, "403");
            description.ShouldBe(expected, operationId);
            description.ShouldStartWith("The user's role on the project is below Administrator, or a scope of the machine is missing (AccessDenied), or the machine is restricted to an annotation (AnnotationRestricted). A machine needs no role but the scope Configuration.Transfer and ", Case.Sensitive, operationId);
            description.ShouldContain(kindScopes, Case.Sensitive, operationId);
            description.ShouldEndWith("Users need no data scopes, only the Administrator role. The message names the missing role or scope.", Case.Sensitive, operationId);

            // the scope strings of the removed presets are gone
            description.ShouldNotContain("Default.", Case.Sensitive, operationId);
            description.ShouldNotContain("ReadWrite", Case.Sensitive, operationId);
            description.ShouldNotContain("Configuration.Secrets", Case.Sensitive, operationId);
        }
    }

    // The 401 of every route is the one of the routes machines and users call: besides missing or invalid credentials, a
    // user token without User.Impersonation, a machine the project does not know, and a credential kind it does not enable.
    [Fact]
    public async Task Document_TheUnauthorizedOfEveryRoute_NamesEveryAuthenticationIssue()
    {
        var json = await OpenApiExportTests.RenderAsync();
        using var document = JsonDocument.Parse(json);

        string[] operationIds =
        [
            Definitions.RouteIds.Transfers.StartViewExport,
            Definitions.RouteIds.Transfers.GetViewExport,
            Definitions.RouteIds.Transfers.GetViewExportArchive,
            Definitions.RouteIds.Transfers.StartViewImport,
            Definitions.RouteIds.Transfers.GetViewImport,
        ];

        foreach (var operationId in operationIds)
        {
            DescriptionOf(document, operationId, "401").ShouldBe(Definitions.Descriptions.ResponseUnauthorized, operationId);
        }

        Definitions.Descriptions.ResponseUnauthorized.ShouldStartWith("Authentication issues: missing or invalid credentials, ", Case.Sensitive);
    }

    [Fact]
    public async Task QueueFunction_ProcessesAValidMessage()
    {
        var functions = new ViewTransferFunctions(_transfers, NullLogger<ViewTransferFunctions>.Instance);
        var message = new ViewTransferMessage("acme", "web", "default", ViewTransferKind.Import, "0123");

        await functions.ProcessViewTransfer(message.ToJson(), CancellationToken.None);

        _transfers.Calls.ShouldBe(["process Import 0123"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"Organization\":\"acme\"}")]
    public async Task QueueFunction_DropsAnInvalidMessage(string message)
    {
        var functions = new ViewTransferFunctions(_transfers, NullLogger<ViewTransferFunctions>.Instance);

        await functions.ProcessViewTransfer(message, CancellationToken.None);

        _transfers.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void QueueFunction_KeepsTheQueueOfTheFirstSpec()
    {
        var trigger = typeof(ViewTransferFunctions).GetMethod(nameof(ViewTransferFunctions.ProcessViewTransfer))!
            .GetParameters()
            .Select(parameter => parameter.GetCustomAttribute<QueueTriggerAttribute>())
            .Single(attribute => attribute is not null)!;

        trigger.QueueName.ShouldBe("configuration-transfers");
    }

    [Fact]
    public async Task PoisonFunction_AbandonsTheJob()
    {
        var functions = new ViewTransferFunctions(_transfers, NullLogger<ViewTransferFunctions>.Instance);
        var message = new ViewTransferMessage("acme", "web", "default", ViewTransferKind.Import, "0123");

        await functions.AbandonViewTransfer(message.ToJson(), CancellationToken.None);
        await functions.AbandonViewTransfer("{", CancellationToken.None);

        _transfers.Calls.ShouldBe(["abandon Import 0123"]);
    }

    [Fact]
    public void PoisonFunction_ListensToThePoisonQueueOfTheJobs()
    {
        var trigger = typeof(ViewTransferFunctions).GetMethod(nameof(ViewTransferFunctions.AbandonViewTransfer))!
            .GetParameters()
            .Select(parameter => parameter.GetCustomAttribute<QueueTriggerAttribute>())
            .Single(attribute => attribute is not null)!;

        trigger.QueueName.ShouldBe("configuration-transfers-poison");
    }

    [Fact]
    public async Task TimerFunction_Purges()
    {
        var functions = new ViewTransferFunctions(_transfers, NullLogger<ViewTransferFunctions>.Instance);

        await functions.PurgeViewTransfers(null!, CancellationToken.None);

        _transfers.Calls.ShouldBe(["purge"]);
    }

    private static string? DescriptionOf(JsonDocument document, string operationId, string status)
    {
        return document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Select(entry => entry.Value)
            .Single(entry => entry.GetProperty("operationId").GetString() == operationId)
            .GetProperty("responses").GetProperty(status).GetProperty("description").GetString();
    }

    private static async Task ShouldFollowTheScopes(Func<Task<IActionResult>> act, bool isAllowed)
    {
        if (isAllowed)
        {
            await act();
            return;
        }

        var exception = await act.ShouldThrowAsync<AccessDeniedException>();
        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldStartWith("Missing scope for ");
        exception.Message.ShouldContain("include");
    }

    // The kinds are checked on what a machine may use: its stored scopes that its credentials carry. Runs the request of
    // a machine twice: with the given scopes carried by the credentials of a machine created with every runtime scope
    // and the management scopes of the import, and with them stored on a machine whose credentials carry all of those.
    // Configuration.Transfer is stored and carried both times; a string that names no machine scope cannot be stored.
    private async Task ShouldFollowTheKindScopes(string scopes, bool isAllowed, Func<ViewTransferHttp, string, Task<IActionResult>> act)
    {
        await ShouldFollowTheScopes(() => act(Http(role: AccountRole.None, managementScopes: TransferManagementScopes), scopes), isAllowed);

        var stored = scopes
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(name => MachineScopes.TryParseScopeString(name, out var scope) ? scope : (MachineScope?)null)
            .OfType<MachineScope>();
        await ShouldFollowTheScopes(() => act(Http(role: AccountRole.None, scopes: [.. stored, MachineScope.ConfigurationTransfer]), CarriedScopes), isAllowed);
    }

    // Starts an export, reads it and downloads its archive, then starts an import and reads it, as a user or a machine.
    private async Task ShouldPassEveryRoute(ViewTransferHttp http, bool machine)
    {
        var zip = EmptyZip();
        var actor = machine ? "machine_01" : "user_01";

        var export = (await http.StartViewExport(Request(Encoding.UTF8.GetBytes("{}"), machine: machine), "acme", "web", "default"))
            .ShouldBeOfType<AcceptedResult>().Value.ShouldBeOfType<ViewExport>();
        (await http.GetViewExport(Request(machine: machine), "acme", "web", "default", export.Id))
            .ShouldBeOfType<OkObjectResult>().Value.ShouldBe(export);
        (await http.GetViewExportArchive(Request(machine: machine), "acme", "web", "default", export.Id))
            .ShouldBeOfType<FileStreamResult>().FileDownloadName.ShouldBe(export.FileName);
        var import = (await http.StartViewImport(Request(zip, machine: machine), "acme", "web", "default"))
            .ShouldBeOfType<AcceptedResult>().Value.ShouldBeOfType<ViewImport>();
        (await http.GetViewImport(Request(machine: machine), "acme", "web", "default", import.Id))
            .ShouldBeOfType<OkObjectResult>().Value.ShouldBe(import);

        _transfers.Calls.ShouldBe(
        [
            $"export acme/web/default Folder all {actor}",
            $"open {export.Id}",
            $"import acme/web/default {zip.Length} bytes  all Overwrite - - {actor}",
        ]);
    }

    // Every route refuses the machine with the code (and the message, when given) before it starts, reads or opens a job.
    // Its credentials carry the given scopes (the kind scopes by default) and the given management scopes.
    private async Task ShouldRefuseEveryRouteToAMachine(ViewTransferHttp http, string machineManagementScopes, string errorCode, string? message, string? scopes = null)
    {
        var export = await _transfers.StartExportAsync("acme", "web", "default", ViewArchiveFormat.Folder, null, AuditActor.System);
        var import = _transfers.AddImport("acme", "web", "default", null);
        _transfers.Calls.Clear();
        _transfers.Reads.Clear();

        (string Route, Func<Task<IActionResult>> Act)[] routes =
        [
            (nameof(ViewTransferHttp.StartViewExport), () => http.StartViewExport(Request(Encoding.UTF8.GetBytes("{}"), scopes: scopes, machine: true, machineManagementScopes: machineManagementScopes), "acme", "web", "default")),
            (nameof(ViewTransferHttp.GetViewExport), () => http.GetViewExport(Request(scopes: scopes, machine: true, machineManagementScopes: machineManagementScopes), "acme", "web", "default", export.Id)),
            (nameof(ViewTransferHttp.GetViewExportArchive), () => http.GetViewExportArchive(Request(scopes: scopes, machine: true, machineManagementScopes: machineManagementScopes), "acme", "web", "default", export.Id)),
            (nameof(ViewTransferHttp.StartViewImport), () => http.StartViewImport(Request(EmptyZip(), scopes: scopes, machine: true, machineManagementScopes: machineManagementScopes), "acme", "web", "default")),
            (nameof(ViewTransferHttp.GetViewImport), () => http.GetViewImport(Request(scopes: scopes, machine: true, machineManagementScopes: machineManagementScopes), "acme", "web", "default", import.Id)),
        ];

        foreach (var (route, act) in routes)
        {
            var exception = await act.ShouldThrowAsync<AccessDeniedException>(route);
            exception.ErrorCode.ShouldBe(errorCode, route);

            if (message is not null)
            {
                exception.Message.ShouldBe(message, route);
            }
        }

        _transfers.Calls.ShouldBeEmpty("a refused machine starts no job and never opens the archive");
        _transfers.Reads.ShouldBeEmpty("a refused machine never reads a job");
    }

    private static byte[] EmptyZip()
    {
        using var stream = new MemoryStream();
        using (new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
        }

        return stream.ToArray();
    }

    private static TestHttpRequestData Request(string body, string? url = null, string? scopes = null, bool machine = false, bool isAnonymous = false)
    {
        return Request(Encoding.UTF8.GetBytes(body), url, scopes: scopes, machine: machine, isAnonymous: isAnonymous);
    }

    // A user acts for a user (User.Impersonation), with the given scopes in scp beside it (none by default: users need
    // none). A machine carries the given scopes as roles (the kind scopes by default, with the management scopes of the
    // import), like the machine validators, and its management scopes too (Configuration.Transfer unless the test names
    // others). The query is parsed as HttpRequestData.Query parses it, so a repeated parameter is joined by commas. An
    // anonymous request has no claims, as one without credentials leaves the authentication middlewares.
    private static TestHttpRequestData Request(
        byte[]? body = null,
        string? url = null,
        Dictionary<string, string>? headers = null,
        string? scopes = null,
        bool machine = false,
        string? query = null,
        bool isAnonymous = false,
        string machineManagementScopes = Scopes.Configuration.Transfer)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestFunctionContext(services);
        var request = new TestHttpRequestData(context, headers, "POST", bodyBytes: body, url: url is null ? null : new Uri(url));
        ((TestInvocationFeatures)context.Features).Set<IHttpRequestDataFeature>(new TestHttpRequestFeature(request));
        request.Query.Add(HttpUtility.ParseQueryString(query ?? string.Empty));

        if (isAnonymous)
        {
            return request;
        }

        var claims = new List<Claim> { new("sub", machine ? "machine_01" : "user_01") };
        claims.AddRange(machine
            ? $"{scopes ?? KindScopes} {machineManagementScopes}".Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(scope => new Claim("roles", scope))
            : [new Claim("scp", $"{Scopes.User.Impersonation} {scopes}".TrimEnd())]);
        context.Items[FunctionContextItemKeys.CachedClaims] = claims;

        if (machine)
        {
            context.Items[FunctionContextItemKeys.MachineIdentity] = "machine_01";
        }

        return request;
    }

    // Without role, the user is an Owner. The machine is created with the six runtime scopes and, without
    // managementScopes, Configuration.Transfer, as a machine that transfers views needs; or with exactly the given scopes.
    private ViewTransferHttp Http(
        ViewTransferOptions? options = null,
        string? machineAnnotationKey = null,
        AccountRole role = AccountRole.Owner,
        IReadOnlyList<MachineScope>? managementScopes = null,
        IReadOnlyList<MachineScope>? scopes = null)
    {
        var access = new RoleAccess(role, machineAnnotationKey, managementScopes ?? [MachineScope.ConfigurationTransfer], scopes);
        var resolved = Options.Create(options ?? new ViewTransferOptions());
        return new ViewTransferHttp(_transfers, access, resolved);
    }

    // Records the calls and the reads of jobs, and keeps the jobs, with their include resolved as the service stores it.
    private sealed class RecordingTransfers : IViewTransferService
    {
        private readonly Dictionary<string, ViewExport> _exports = [];
        private readonly Dictionary<string, ViewImport> _imports = [];

        public List<string> Calls { get; } = [];

        public List<string> Reads { get; } = [];

        public ViewImport AddImport(string organization, string project, string view, IReadOnlyList<ViewArchiveContent>? include)
        {
            var import = new ViewImport
            {
                Id = Guid.NewGuid().ToString("N"),
                Status = ViewTransferStatus.Succeeded,
                Include = include,
                CreatedAt = Now,
                CreatedBy = AuditActor.System,
                ExpiresAt = Now.AddHours(24),
            };
            _imports[$"{organization}/{project}/{view}/{import.Id}"] = import;
            return import;
        }

        public Task<ViewExport> StartExportAsync(string organization, string project, string view, ViewArchiveFormat format, IReadOnlyList<ViewArchiveContent>? include, AuditActor actor, CancellationToken cancellationToken = default)
        {
            Calls.Add($"export {organization}/{project}/{view} {format} {Describe(include)} {actor.Id}");
            var id = Guid.NewGuid().ToString("N");
            var export = new ViewExport
            {
                Id = id,
                Status = ViewTransferStatus.Succeeded,
                Format = format,
                Include = ViewArchiveInclude.Resolve(include),
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
            Reads.Add($"export {id}");
            return Task.FromResult(_exports.GetValueOrDefault($"{organization}/{project}/{view}/{id}"));
        }

        public async Task<(ViewExport Export, Stream Archive)> OpenExportArchiveAsync(string organization, string project, string view, string id, CancellationToken cancellationToken = default)
        {
            var export = await GetExportAsync(organization, project, view, id, cancellationToken) ?? throw new NotFoundException("missing");
            Calls.Add($"open {id}");
            return (export, new MemoryStream([1, 2, 3]));
        }

        public Task<ViewImport> StartImportAsync(string organization, string project, string view, Stream archive, string? fileName, IReadOnlyList<ViewArchiveContent>? include, ViewImportExisting existing, bool force, bool dryRun, AuditActor actor, CancellationToken cancellationToken = default)
        {
            Calls.Add($"import {organization}/{project}/{view} {archive.Length - archive.Position} bytes {fileName} {Describe(include)} {existing} {(force ? "force" : "-")} {(dryRun ? "dry-run" : "-")} {actor.Id}");
            var import = new ViewImport
            {
                Id = Guid.NewGuid().ToString("N"),
                Status = ViewTransferStatus.Queued,
                FileName = fileName,
                Include = ViewArchiveInclude.Resolve(include),
                Existing = existing,
                IsDryRun = dryRun,
                IsForced = force,
                CreatedAt = Now,
                CreatedBy = actor,
                ExpiresAt = Now.AddHours(24),
            };
            _imports[$"{organization}/{project}/{view}/{import.Id}"] = import;
            return Task.FromResult(import);
        }

        public Task<ViewImport?> GetImportAsync(string organization, string project, string view, string id, CancellationToken cancellationToken = default)
        {
            Reads.Add($"import {id}");
            return Task.FromResult(_imports.GetValueOrDefault($"{organization}/{project}/{view}/{id}"));
        }

        public Task ProcessAsync(ViewTransferMessage message, CancellationToken cancellationToken = default)
        {
            Calls.Add($"process {message.Kind} {message.Id}");
            return Task.CompletedTask;
        }

        public Task AbandonAsync(ViewTransferMessage message, CancellationToken cancellationToken = default)
        {
            Calls.Add($"abandon {message.Kind} {message.Id}");
            return Task.CompletedTask;
        }

        public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("purge");
            return Task.FromResult(0);
        }

        private static string Describe(IReadOnlyList<ViewArchiveContent>? include)
        {
            return include is null ? "all" : string.Join(',', include);
        }
    }
}
