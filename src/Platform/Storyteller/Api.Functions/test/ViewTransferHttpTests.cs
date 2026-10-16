using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
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

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The routes of the export and import of a view: the options and their defaults, the scopes per included kind (on
// requests and on reading jobs), and the background functions. Debug builds skip the scope checks (DEV_AUTH), so the
// tests of the scopes turn them on (ChecksScopes); the role checks are the shared helpers' and pass for an Owner.
public class ViewTransferHttpTests
{
    private const string AllScopes = "Default.ReadWrite";

    private static readonly DateTimeOffset Now = new(2026, 10, 16, 1, 30, 12, TimeSpan.Zero);

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

    // Scopes per kind: the rule of each kind, for an export and reading it, and for an import and reading it.
    [Theory]
    [InlineData(ViewArchiveContent.Annotations, ViewTransferKind.Export, new[] { Scopes.Annotation.Read, Scopes.Annotation.Write, Scopes.Default.Read, Scopes.Default.Write })]
    [InlineData(ViewArchiveContent.Schemas, ViewTransferKind.Export, new[] { Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Default.Read, Scopes.Default.Write })]
    [InlineData(ViewArchiveContent.Templates, ViewTransferKind.Export, new[] { Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Default.Read, Scopes.Default.Write })]
    [InlineData(ViewArchiveContent.Configurations, ViewTransferKind.Export, new[] { Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Default.Read, Scopes.Default.Write })]
    [InlineData(ViewArchiveContent.Annotations, ViewTransferKind.Import, new[] { Scopes.Annotation.Write, Scopes.Default.Write })]
    [InlineData(ViewArchiveContent.Schemas, ViewTransferKind.Import, new[] { Scopes.Configuration.Write, Scopes.Default.Write })]
    [InlineData(ViewArchiveContent.Templates, ViewTransferKind.Import, new[] { Scopes.Configuration.Write, Scopes.Default.Write })]
    [InlineData(ViewArchiveContent.Configurations, ViewTransferKind.Import, new[] { Scopes.Configuration.Write, Scopes.Default.Write })]
    public void Scopes_OfEachKind(ViewArchiveContent content, ViewTransferKind kind, string[] expected)
    {
        ViewTransferScopes.GetScopes(content, kind).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Default.Read", null, true)]
    [InlineData("Default.ReadWrite", null, true)]
    [InlineData("Annotation.Read Configuration.Read", null, true)]
    [InlineData("Annotation.ReadWrite Configuration.ReadWrite", null, true)]
    [InlineData("Configuration.Read", null, false)]
    [InlineData("Configuration.Read", "Schemas,Templates,Configurations", true)]
    [InlineData("Configuration.ReadWrite", "Configurations", true)]
    [InlineData("Configuration.Read", "Annotations", false)]
    [InlineData("Annotation.Read", "Annotations", true)]
    [InlineData("Annotation.ReadWrite", "Annotations", true)]
    [InlineData("Annotation.Read", "Annotations,Templates", false)]
    [InlineData("User.Impersonation", "Configurations", false)]
    [InlineData("", "Schemas", false)]
    public async Task StartExport_NeedsTheScopesOfEveryIncludedKind(string scopes, string? include, bool isAllowed)
    {
        var body = include is null ? string.Empty : $$"""{ "Include": [ {{string.Join(", ", include.Split(',').Select(kind => $"\"{kind}\""))}} ] }""";
        var act = () => Http(checksScopes: true).StartViewExport(Request(body, scopes: scopes), "acme", "web", "default");

        await ShouldFollowTheScopes(act, isAllowed);
    }

    [Theory]
    [InlineData("Default.ReadWrite", null, true)]
    [InlineData("Annotation.ReadWrite Configuration.ReadWrite", null, true)]
    [InlineData("Default.Read", null, false)]
    [InlineData("Configuration.ReadWrite", null, false)]
    [InlineData("Configuration.ReadWrite", "schemas,templates,configurations", true)]
    [InlineData("Configuration.Read", "configurations", false)]
    [InlineData("Configuration.ReadWrite", "annotations", false)]
    [InlineData("Annotation.ReadWrite", "annotations", true)]
    [InlineData("Annotation.Read Configuration.ReadWrite", "annotations,configurations", false)]
    [InlineData("Annotation.ReadWrite", "annotations,templates", false)]
    public async Task StartImport_NeedsTheScopesOfEveryIncludedKind(string scopes, string? include, bool isAllowed)
    {
        var act = () => Http(checksScopes: true).StartViewImport(Request(EmptyZip(), scopes: scopes, query: include is null ? null : $"include={include}"), "acme", "web", "default");

        await ShouldFollowTheScopes(act, isAllowed);
    }

    [Fact]
    public async Task StartImport_ByAMachineWithOnlyConfigurationReadWrite_NamesTheMissingScopeAndInclude()
    {
        var act = () => Http(checksScopes: true).StartViewImport(Request(EmptyZip(), scopes: Scopes.Configuration.Write, machine: true), "acme", "web", "default");

        var exception = await act.ShouldThrowAsync<AccessDeniedException>();
        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("Missing scope for annotations: importing them needs Annotation.ReadWrite or Default.ReadWrite. Without include, all four kinds are included; name the kinds to import in include.");
        _transfers.Calls.ShouldBeEmpty();

        await Http(checksScopes: true).StartViewImport(Request(EmptyZip(), scopes: Scopes.Configuration.Write, machine: true, query: "include=schemas,templates,configurations"), "acme", "web", "default");
        _transfers.Calls.ShouldHaveSingleItem().ShouldContain("Schemas,Templates,Configurations Overwrite");
    }

    [Fact]
    public async Task StartExport_WithAnIncludedKindWithoutItsScope_NamesTheScopeAndInclude()
    {
        var act = () => Http(checksScopes: true).StartViewExport(Request("""{ "Include": [ "Annotations", "Templates" ] }""", scopes: Scopes.Annotation.Read), "acme", "web", "default");

        var exception = await act.ShouldThrowAsync<AccessDeniedException>();
        exception.Message.ShouldBe("Missing scope for templates in include: exporting them needs Configuration.Read, Configuration.ReadWrite, Default.Read or Default.ReadWrite.");
    }

    [Fact]
    public void Scopes_WithoutAnyKind_NameEveryMissingRule()
    {
        var exception = Should.Throw<AccessDeniedException>(() => ViewTransferScopes.Ensure(new HashSet<string> { Scopes.User.Impersonation }, null, ViewTransferKind.Export));

        exception.Message.ShouldBe(
            "Missing scope for annotations: exporting them needs Annotation.Read, Annotation.ReadWrite, Default.Read or Default.ReadWrite. "
            + "Missing scope for schemas, templates and configurations: exporting them needs Configuration.Read, Configuration.ReadWrite, Default.Read or Default.ReadWrite. "
            + "Without include, all four kinds are included; name the kinds to export in include.");
    }

    // Reading a job checks the scopes of the include stored with it, not those of the request.
    [Theory]
    [InlineData("Configuration.Read", new[] { ViewArchiveContent.Configurations }, true)]
    [InlineData("Configuration.Read", new[] { ViewArchiveContent.Annotations, ViewArchiveContent.Configurations }, false)]
    [InlineData("Annotation.Read", new[] { ViewArchiveContent.Annotations }, true)]
    [InlineData("Default.Read", new[] { ViewArchiveContent.Annotations, ViewArchiveContent.Schemas, ViewArchiveContent.Templates, ViewArchiveContent.Configurations }, true)]
    public async Task GetExport_NeedsTheScopesOfTheKindsTheExportIncludes(string scopes, ViewArchiveContent[] include, bool isAllowed)
    {
        var export = await _transfers.StartExportAsync("acme", "web", "default", ViewArchiveFormat.Folder, include, AuditActor.System);
        _transfers.Calls.Clear();

        await ShouldFollowTheScopes(() => Http(checksScopes: true).GetViewExport(Request(scopes: scopes), "acme", "web", "default", export.Id), isAllowed);
        await ShouldFollowTheScopes(() => Http(checksScopes: true).GetViewExportArchive(Request(scopes: scopes), "acme", "web", "default", export.Id), isAllowed);

        string[] expected = isAllowed ? [$"open {export.Id}"] : [];
        _transfers.Calls.ShouldBe(expected, ignoreOrder: false, "a denied download never opens the archive");
    }

    [Fact]
    public async Task GetExport_WithoutTheScopeOfAnIncludedKind_NamesItAndTheJobsInclude()
    {
        var export = await _transfers.StartExportAsync("acme", "web", "default", ViewArchiveFormat.Git, [ViewArchiveContent.Annotations], AuditActor.System);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => Http(checksScopes: true).GetViewExport(Request(scopes: Scopes.Configuration.Read), "acme", "web", "default", export.Id));

        exception.Message.ShouldBe("Missing scope for annotations in the include of this export: reading it needs Annotation.Read, Annotation.ReadWrite, Default.Read or Default.ReadWrite.");
    }

    [Theory]
    [InlineData("Configuration.ReadWrite", new[] { ViewArchiveContent.Schemas, ViewArchiveContent.Configurations }, true)]
    [InlineData("Configuration.ReadWrite", new[] { ViewArchiveContent.Annotations, ViewArchiveContent.Configurations }, false)]
    [InlineData("Configuration.Read", new[] { ViewArchiveContent.Configurations }, false)]
    [InlineData("Annotation.ReadWrite", new[] { ViewArchiveContent.Annotations }, true)]
    [InlineData("Default.ReadWrite", new[] { ViewArchiveContent.Annotations, ViewArchiveContent.Schemas, ViewArchiveContent.Templates, ViewArchiveContent.Configurations }, true)]
    public async Task GetImport_NeedsTheScopesOfTheKindsTheImportIncludes(string scopes, ViewArchiveContent[] include, bool isAllowed)
    {
        var import = _transfers.AddImport("acme", "web", "default", include);

        await ShouldFollowTheScopes(() => Http(checksScopes: true).GetViewImport(Request(scopes: scopes), "acme", "web", "default", import.Id), isAllowed);
    }

    [Fact]
    public async Task GetImport_OfAJobWithoutInclude_NeedsTheScopesOfAllFourKinds()
    {
        var import = _transfers.AddImport("acme", "web", "default", null);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => Http(checksScopes: true).GetViewImport(Request(scopes: Scopes.Configuration.Write), "acme", "web", "default", import.Id));

        exception.Message.ShouldBe("Missing scope for annotations in the include of this import: reading it needs Annotation.ReadWrite or Default.ReadWrite.");
    }

    // Debug builds (DEV_AUTH) skip the scope checks like every other route; a Release build enforces them.
    [Fact]
    public async Task Scopes_AreCheckedUnlessDevelopmentAuthentication()
    {
        var http = Http();
        http.ChecksScopes.ShouldBe(!DevAuth);

        var act = () => http.StartViewExport(Request(scopes: Scopes.User.Impersonation), "acme", "web", "default");

        if (DevAuth)
        {
            (await act()).ShouldBeOfType<AcceptedResult>();
        }
        else
        {
            await act.ShouldThrowAsync<AccessDeniedException>();
        }
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
    public async Task TimerFunction_Purges()
    {
        var functions = new ViewTransferFunctions(_transfers, NullLogger<ViewTransferFunctions>.Instance);

        await functions.PurgeViewTransfers(null!, CancellationToken.None);

        _transfers.Calls.ShouldBe(["purge"]);
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

    private static byte[] EmptyZip()
    {
        using var stream = new MemoryStream();
        using (new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
        }

        return stream.ToArray();
    }

    private static TestHttpRequestData Request(string body, string? url = null, string scopes = AllScopes)
    {
        return Request(Encoding.UTF8.GetBytes(body), url, scopes: scopes);
    }

    // A user with the given scp; a machine carries them as roles, like the machine validators. The query is parsed as
    // HttpRequestData.Query parses it, so a repeated parameter is joined by commas.
    private static TestHttpRequestData Request(
        byte[]? body = null,
        string? url = null,
        Dictionary<string, string>? headers = null,
        string scopes = AllScopes,
        bool machine = false,
        string? query = null)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestFunctionContext(services);
        var request = new TestHttpRequestData(context, headers, "POST", bodyBytes: body, url: url is null ? null : new Uri(url));
        ((TestInvocationFeatures)context.Features).Set<IHttpRequestDataFeature>(new TestHttpRequestFeature(request));
        request.Query.Add(HttpUtility.ParseQueryString(query ?? string.Empty));

        var claims = new List<Claim> { new("sub", machine ? "machine_01" : "user_01") };
        claims.AddRange(machine
            ? scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(scope => new Claim("roles", scope))
            : [new Claim("scp", scopes)]);
        context.Items[FunctionContextItemKeys.CachedClaims] = claims;

        if (machine)
        {
            context.Items[FunctionContextItemKeys.MachineIdentity] = "machine_01";
        }

        return request;
    }

    // Without checksScopes, the class default: no scope checks in a Debug build.
    private ViewTransferHttp Http(ViewTransferOptions? options = null, bool? checksScopes = null)
    {
        var access = OwnerAccess.Create();
        var resolved = Options.Create(options ?? new ViewTransferOptions());
        return checksScopes is { } checks
            ? new ViewTransferHttp(_transfers, access, resolved) { ChecksScopes = checks }
            : new ViewTransferHttp(_transfers, access, resolved);
    }

    // Records the calls and keeps the jobs, with their include resolved as the service stores it.
    private sealed class RecordingTransfers : IViewTransferService
    {
        private readonly Dictionary<string, ViewExport> _exports = [];
        private readonly Dictionary<string, ViewImport> _imports = [];

        public List<string> Calls { get; } = [];

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
            return Task.FromResult(_imports.GetValueOrDefault($"{organization}/{project}/{view}/{id}"));
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

        private static string Describe(IReadOnlyList<ViewArchiveContent>? include)
        {
            return include is null ? "all" : string.Join(',', include);
        }
    }

    // The caller is an Owner of every project, and a machine has access to it; the role checks themselves are the
    // shared helpers' (skipped in a Debug build).
    public class OwnerAccess : DispatchProxy
    {
        public static IAccessService Create() => Create<IAccessService, OwnerAccess>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                nameof(IAccessService.GetAccountRoleAsync) => Task.FromResult(AccountRole.Owner),
                nameof(IAccessService.VerifyAccessForMachineAsync) => Task.FromResult(true),
                _ => throw new NotSupportedException(targetMethod?.Name),
            };
        }
    }
}
