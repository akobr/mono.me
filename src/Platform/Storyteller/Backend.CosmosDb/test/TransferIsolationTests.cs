#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Transferring;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Tenant isolation of the view export and import jobs in the shared container "organizations": two organizations with
// the same project, views, annotation keys and job ids, and different values ("alpha" in A, "bravo" in B). Whatever
// runs through one organization reads only its own data and never changes the other's. Also the organization catalog,
// which reads the organizations from core. Every test seeds its own pair of organizations.
public class TransferIsolationTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Project = "billing";
    private const string View = Constants.DefaultViewName;
    private const string OtherView = "prod";
    private const string AlphaMarker = "alpha";
    private const string BravoMarker = "bravo";
    private const string Owner = "iso-transfers-owner";

    // xUnit creates the class for every test, so every test has organizations of its own
    private readonly string _run = Guid.NewGuid().ToString("N")[..8];

    private string Alpha => $"iso-transfers-a-{_run}";

    private string Bravo => $"iso-transfers-b-{_run}";

    private IViewTransferStore Store => Context.Services.GetRequiredService<IViewTransferStore>();

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    private IConfigurationService Configurations => Context.Services.GetRequiredService<IConfigurationService>();

    private IConfigurationTemplateService Templates => Context.Services.GetRequiredService<IConfigurationTemplateService>();

    private IConfigurationSchemaService Schemas => Context.Services.GetRequiredService<IConfigurationSchemaService>();

    [Fact]
    public async Task Store_KeepsTheJobsOfEachOrganizationApart()
    {
        var id = Guid.NewGuid().ToString("N");
        var bravoOnly = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var since = now.AddMinutes(-5);

        // the same job id in the same partition value of both organizations: an id is unique per full key
        var alpha = await Store.CreateAsync(Job(Alpha, id, AlphaMarker, now));
        var bravo = await Store.CreateAsync(Job(Bravo, id, BravoMarker, now));
        var bravoSecond = await Store.CreateAsync(Job(Bravo, bravoOnly, BravoMarker, now));

        (await OrganizationsOfItemAsync(CosmosViewTransferStore.GetId(View, ViewTransferKind.Export, id))).Should().BeEquivalentTo(Alpha, Bravo);
        (await Store.GetAsync(Alpha, Project, View, ViewTransferKind.Export, id))!.Export!.FileName.Should().Be("alpha-configurations.zip");
        (await Store.GetAsync(Bravo, Project, View, ViewTransferKind.Export, id))!.Export!.FileName.Should().Be("bravo-configurations.zip");
        (await Store.GetAsync(Alpha, Project, View, ViewTransferKind.Export, bravoOnly)).Should().BeNull();
        (await Store.CountActiveAsync(Alpha, Project, since, since)).Should().Be(1);
        (await Store.CountActiveAsync(Bravo, Project, since, since)).Should().Be(2);

        // A's job runs and ends; B's job of the same id stays queued, unwritten
        var running = await Store.TryReplaceAsync(alpha with { Export = alpha.Export! with { Status = ViewTransferStatus.Running } });
        var succeeded = await Store.TryReplaceAsync(running! with { Export = running.Export! with { Status = ViewTransferStatus.Succeeded } });
        succeeded.Should().NotBeNull();

        (await Store.CountActiveAsync(Alpha, Project, since, since)).Should().Be(0);
        (await Store.CountActiveAsync(Bravo, Project, since, since)).Should().Be(2);
        var bravoAfter = await Store.GetAsync(Bravo, Project, View, ViewTransferKind.Export, id);
        bravoAfter!.ETag.Should().Be(bravo.ETag);
        bravoAfter.Status.Should().Be(ViewTransferStatus.Queued);

        // a replace through A never reaches B's items: a job only B has is missing in A, and B's ETag is not the one of
        // A's job under the same id
        var failedOnly = bravoSecond with { Organization = Alpha, Export = bravoSecond.Export! with { Status = ViewTransferStatus.Failed } };
        var failedSame = bravo with { Organization = Alpha, Export = bravo.Export! with { Status = ViewTransferStatus.Failed } };
        (await Store.TryReplaceAsync(failedOnly)).Should().BeNull();
        (await Store.TryReplaceAsync(failedSame)).Should().BeNull();

        (await Store.GetAsync(Alpha, Project, View, ViewTransferKind.Export, bravoOnly)).Should().BeNull();
        var alphaAfter = await Store.GetAsync(Alpha, Project, View, ViewTransferKind.Export, id);
        alphaAfter!.ETag.Should().Be(succeeded!.ETag);
        alphaAfter.Status.Should().Be(ViewTransferStatus.Succeeded);
        var bravoSecondAfter = await Store.GetAsync(Bravo, Project, View, ViewTransferKind.Export, bravoOnly);
        bravoSecondAfter!.ETag.Should().Be(bravoSecond.ETag);
        bravoSecondAfter.Status.Should().Be(ViewTransferStatus.Queued);
        (await Store.GetAsync(Bravo, Project, View, ViewTransferKind.Export, id))!.ETag.Should().Be(bravo.ETag);
    }

    [Fact]
    public async Task Export_HoldsOnlyTheDocumentsOfItsOrganization()
    {
        await SeedBothAsync();
        var transfers = new Transfers(Context.Services);

        var alpha = await transfers.ExportAsync(Alpha, View, Author(AlphaMarker));
        var bravo = await transfers.ExportAsync(Bravo, View, Author(BravoMarker));

        // B has one annotation more under the same project and view
        Counts(alpha.Job).Should().Equal(2, 1, 1, 1);
        Counts(bravo.Job).Should().Equal(3, 1, 1, 1);

        var alphaText = ReadText(alpha.Archive);
        alphaText.Should().Contain("alpha Payments").And.Contain("alpha-gold").And.Contain("alpha zone").And.Contain("alpha-timeout").And.Contain("alpha-team");
        alphaText.Should().NotContain(BravoMarker).And.NotContain(Bravo);

        var bravoText = ReadText(bravo.Archive);
        bravoText.Should().Contain("bravo Payments").And.Contain("rst.bravo-extra").And.Contain("bravo zone").And.Contain("bravo-timeout").And.Contain("bravo-team");
        bravoText.Should().NotContain(AlphaMarker).And.NotContain(Alpha);

        // a job is found only in the organization that started it
        (await transfers.Service.GetExportAsync(Alpha, Project, View, alpha.Job.Id)).Should().NotBeNull();
        (await transfers.Service.GetExportAsync(Bravo, Project, View, alpha.Job.Id)).Should().BeNull();
        (await transfers.Service.GetExportAsync(Alpha, Project, View, bravo.Job.Id)).Should().BeNull();
        var open = () => transfers.Service.OpenExportArchiveAsync(Bravo, Project, View, alpha.Job.Id);
        await open.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Import_WritesOnlyIntoItsOrganization()
    {
        await SeedBothAsync();
        var transfers = new Transfers(Context.Services);
        var bravoDefault = await SnapshotAsync(Bravo, View);
        var bravoProd = await SnapshotAsync(Bravo, OtherView);
        bravoProd.Should().Contain(line => line.Contains("bravo-team"));

        // a copy of A's view into A's prod, which only B has: with Skip, a document of B's prod that counted as
        // existing would be skipped
        var export = await transfers.ExportAsync(Alpha, View, Author(AlphaMarker));
        var copy = await transfers.ImportAsync(Alpha, OtherView, export.Archive, ViewImportExisting.Skip, Author(AlphaMarker));

        copy.Error.Should().BeNull();
        copy.Status.Should().Be(ViewTransferStatus.Succeeded);
        var copySummary = copy.Summary!;
        (copySummary.Created, copySummary.Skipped, copySummary.Failed).Should().Be((5, 0, 0));
        (await Annotations.GetAnnotationAsync(Key(Alpha, "rst.payments", OtherView)))!.Title.Should().Be("alpha Payments");

        // an overwrite of A's documents that B has under the same keys
        var overwrite = await transfers.ImportAsync(
            Alpha,
            View,
            Zip(
                ("annotations/rst.payments.json", """{"Title":"alpha imported"}"""),
                ("templates/exe.template.json", """{"timeout":"alpha-imported"}"""),
                ("configurations/rst.payments.conf.json", """{"owner":"alpha-imported"}""")),
            ViewImportExisting.Overwrite,
            Author(AlphaMarker));

        overwrite.Error.Should().BeNull();
        overwrite.Status.Should().Be(ViewTransferStatus.Succeeded);
        var overwriteSummary = overwrite.Summary!;
        (overwriteSummary.Updated, overwriteSummary.Failed).Should().Be((3, 0));
        (await Configurations.GetConfigurationVersionsAsync(Key(Alpha, "rst.payments", View))).Select(version => version.Version).Should().Equal(1u, 2u);
        (await Configurations.GetConfigurationVersionContentAsync(Key(Alpha, "rst.payments", View), 2))!.Content["owner"]!.Value<string>().Should().Be("alpha-imported");
        (await Annotations.GetAnnotationAsync(Key(Alpha, "rst.payments", View)))!.Title.Should().Be("alpha imported");

        // B: both views as they were, and A's jobs are not B's
        (await SnapshotAsync(Bravo, View)).Should().Equal(bravoDefault);
        (await SnapshotAsync(Bravo, OtherView)).Should().Equal(bravoProd);
        (await Configurations.GetConfigurationVersionsAsync(Key(Bravo, "rst.payments", View))).Select(version => version.Version).Should().Equal(1u);
        (await Configurations.GetConfigurationVersionContentAsync(Key(Bravo, "rst.payments", View), 1))!.Content["owner"]!.Value<string>().Should().Be("bravo-team");
        (await Annotations.GetAnnotationAsync(Key(Bravo, "rst.payments", View)))!.Title.Should().Be("bravo Payments");
        (await transfers.Service.GetImportAsync(Bravo, Project, OtherView, copy.Id)).Should().BeNull();
        (await transfers.Service.GetImportAsync(Bravo, Project, View, overwrite.Id)).Should().BeNull();
    }

    [Fact]
    public async Task StartExport_CountsTheActiveJobsOfTheProjectInItsOrganizationOnly()
    {
        var transfers = new Transfers(Context.Services);
        var limit = new ViewTransferOptions().MaxActiveTransfers;
        var since = DateTimeOffset.UtcNow.AddMinutes(-5);

        // B's project is full of queued exports that no worker has taken yet
        for (var index = 0; index < limit; index++)
        {
            await transfers.Service.StartExportAsync(Bravo, Project, View, ViewArchiveFormat.Folder, null, Author(BravoMarker));
        }

        var overBravo = () => transfers.Service.StartExportAsync(Bravo, Project, View, ViewArchiveFormat.Folder, null, Author(BravoMarker));
        (await overBravo.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.TransferLimit);

        // the project of the same name in A has a limit of its own
        for (var index = 0; index < limit; index++)
        {
            await transfers.Service.StartExportAsync(Alpha, Project, View, ViewArchiveFormat.Folder, null, Author(AlphaMarker));
        }

        var overAlpha = () => transfers.Service.StartExportAsync(Alpha, Project, View, ViewArchiveFormat.Folder, null, Author(AlphaMarker));
        (await overAlpha.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.TransferLimit);

        (await Store.CountActiveAsync(Alpha, Project, since, since)).Should().Be(limit);
        (await Store.CountActiveAsync(Bravo, Project, since, since)).Should().Be(limit);
    }

    [Fact]
    public async Task Worker_TakesOnlyTheJobOfTheOrganizationOfItsMessage()
    {
        await SeedAsync(Bravo, BravoMarker, View);
        var transfers = new Transfers(Context.Services);
        var queued = await transfers.Service.StartExportAsync(Bravo, Project, View, ViewArchiveFormat.Folder, null, Author(BravoMarker));
        var before = await Store.GetAsync(Bravo, Project, View, ViewTransferKind.Export, queued.Id);

        // a message of A with the id of B's job: A has no such job, so the worker neither runs nor fails anything
        var stray = new ViewTransferMessage(Alpha, Project, View, ViewTransferKind.Export, queued.Id);
        await transfers.Service.ProcessAsync(stray);
        await transfers.Service.AbandonAsync(stray);

        var after = await Store.GetAsync(Bravo, Project, View, ViewTransferKind.Export, queued.Id);
        after!.ETag.Should().Be(before!.ETag);
        after.Status.Should().Be(ViewTransferStatus.Queued);
        (await Store.GetAsync(Alpha, Project, View, ViewTransferKind.Export, queued.Id)).Should().BeNull();

        // B's own message runs B's job
        await transfers.Service.ProcessAsync(transfers.LastMessage);

        var done = await transfers.Service.GetExportAsync(Bravo, Project, View, queued.Id);
        done!.Error.Should().BeNull();
        done.Status.Should().Be(ViewTransferStatus.Succeeded);
        Counts(done).Should().Equal(2, 1, 1, 1);
    }

    [Fact]
    public async Task OrganizationCatalog_ListsTheOrganizationsOfCoreWithoutTheirProjects()
    {
        var access = Context.Services.GetRequiredService<IAccessService>();
        var catalog = Context.Services.GetRequiredService<CosmosOrganizationCatalog>();
        var dataOnly = $"iso-transfers-c-{_run}";

        await access.CreateAccessPointAsync(new AccessPointCreate { Organization = Alpha, Project = Project, OwnerId = Owner });
        await access.CreateAccessPointAsync(new AccessPointCreate { Organization = Bravo, Project = Project, OwnerId = Owner });
        await access.CreateAccessPointAsync(new AccessPointCreate { Organization = Bravo, Project = "shipping", OwnerId = Owner });

        // data in the shared container does not make an organization: core is the record of them
        await Store.CreateAsync(Job(dataOnly, Guid.NewGuid().ToString("N"), "charlie", DateTimeOffset.UtcNow));

        var organizations = await catalog.ListAsync();

        organizations.Should().Contain(Alpha).And.Contain(Bravo);
        organizations.Should()
            .NotContain($"{Alpha}.{Project}")
            .And.NotContain($"{Bravo}.{Project}")
            .And.NotContain($"{Bravo}.shipping")
            .And.NotContain(dataOnly);
        organizations.Should().OnlyContain(organization => !organization.Contains('.'));
        organizations.Should().OnlyHaveUniqueItems();
        organizations.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    private static ViewTransferRecord Job(string organization, string id, string marker, DateTimeOffset now)
    {
        return new ViewTransferRecord
        {
            Organization = organization,
            Project = Project,
            View = View,
            Kind = ViewTransferKind.Export,
            Id = id,
            Export = new ViewExport
            {
                Id = id,
                Status = ViewTransferStatus.Queued,
                Format = ViewArchiveFormat.Folder,
                FileName = $"{marker}-configurations.zip",
                CreatedAt = now,
                CreatedBy = Author(marker),
                ExpiresAt = now.AddHours(24),
            },
        };
    }

    private static AuditActor Author(string marker)
    {
        return AuditActor.Account($"user_{marker}", $"{marker} writer");
    }

    private static JObject Schema(string marker)
    {
        return new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["zone"] = new JObject { ["type"] = "string", ["description"] = $"{marker} zone" },
            },
        };
    }

    private static int?[] Counts(ViewExport export)
    {
        return [export.AnnotationCount, export.SchemaCount, export.TemplateCount, export.ConfigurationCount];
    }

    private static FullKey Key(string organization, string annotationKey, string view)
    {
        return FullKey.Create(AnnotationKey.Parse(annotationKey), organization, Project, view);
    }

    private static byte[] Zip(params (string Path, string Content)[] files)
    {
        using var buffer = new MemoryStream();

        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }

    // The path and the text of every entry of a zip, one after another.
    private static string ReadText(byte[] archive)
    {
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        var text = new StringBuilder();

        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            text.AppendLine(entry.FullName).AppendLine(reader.ReadToEnd());
        }

        return text.ToString();
    }

    private static IEnumerable<string> Lines(string name, ConfigurationDocument? current, IReadOnlyList<ConfigurationDocument> versions)
    {
        return versions.Select(version =>
            $"{name} (current {current?.Version}) v{version.Version} by {version.Author} at {version.CreatedAt:O}: {version.Content.ToString(Formatting.None)}");
    }

    // The same documents under the same keys in every organization, each value with the organization's marker: two
    // annotations, a type schema, a template and a configuration.
    private async Task SeedAsync(string organization, string marker, string view)
    {
        var author = Author(marker);
        await Annotations.CreateAnnotationsFromStringAsync(organization, Project, view, new[] { "unt.payments.api" }, author);
        await Annotations.SetAnnotationAsync(
            organization,
            AnnotationFactory.CreateDefault(AnnotationKey.Parse("rst.payments"), Project, view) with
            {
                Title = $"{marker} Payments",
                Values = new Dictionary<string, object> { ["tier"] = $"{marker}-gold" },
            },
            author);
        await Annotations.SetAnnotationAsync(
            organization,
            AnnotationFactory.CreateDefault(AnnotationKey.Parse("unt.payments.api"), Project, view) with { Title = $"{marker} API" },
            author);

        await Schemas.SetSchemaAsync(organization, Project, view, AnnotationTypeCodes.Execution, Schema(marker), author, false);
        await Templates.CreateOrUpdateTemplateAsync(organization, Project, view, AnnotationTypeCodes.Execution, new JObject { ["timeout"] = $"{marker}-timeout" }, author);
        await Configurations.CreateOrUpdateConfigurationAsync(Key(organization, "rst.payments", view), new JObject { ["owner"] = $"{marker}-team" }, author);
    }

    // A in its default view; B in both views and with one more annotation in the default one.
    private async Task SeedBothAsync()
    {
        await SeedAsync(Alpha, AlphaMarker, View);
        await SeedAsync(Bravo, BravoMarker, View);
        await SeedAsync(Bravo, BravoMarker, OtherView);
        await Annotations.CreateAnnotationsFromStringAsync(Bravo, Project, View, new[] { "rst.bravo-extra" }, Author(BravoMarker));
    }

    // What a view of an organization holds, a line per annotation and per version of a configuration, template or
    // schema, in ordinal order: any write into the view changes a line or adds one.
    private async Task<IReadOnlyList<string>> SnapshotAsync(string organization, string view)
    {
        var lines = new List<string>();

        foreach (var annotation in await Annotations.GetViewAnnotationsAsync(organization, Project, view))
        {
            var values = annotation.Values is null
                ? string.Empty
                : string.Join(",", annotation.Values.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));
            lines.Add($"annotation {annotation.AnnotationKey} '{annotation.Title}' {{{values}}} modified {annotation.Modified?.At:O}");
        }

        foreach (var history in await Configurations.GetConfigurationHistoriesAsync(organization, Project, view))
        {
            lines.AddRange(Lines($"configuration {history.AnnotationKey}", history.Current, history.Versions));
        }

        foreach (var history in await Templates.GetTemplateHistoriesAsync(organization, Project, view))
        {
            lines.AddRange(Lines($"template {history.AnnotationType}", history.Current, history.Versions));
        }

        foreach (var history in await Schemas.GetSchemaHistoriesAsync(organization, Project, view))
        {
            lines.AddRange(Lines($"schema {history.Kind} {history.AnnotationType} {history.AnnotationKey}", history.Current, history.Versions));
        }

        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    // The organizations of the items with the id, over every partition of the shared container.
    private async Task<IReadOnlyList<string>> OrganizationsOfItemAsync(string id)
    {
        // the container of any organization is the shared one; a query without a partition key spans all of them
        var container = Context.Services.GetRequiredService<IContainerRepositoryProvider>().GetOrganizationContainer(Alpha).Container;
        var query = new QueryDefinition("SELECT VALUE c.Organization FROM c WHERE c.id = @id").WithParameter("@id", id);
        var organizations = new List<string>();

        using var crossOrganization = OrganizationQueryGuard.AllowCrossOrganization();
        using var iterator = container.GetItemQueryIterator<string>(query);

        while (iterator.HasMoreResults)
        {
            organizations.AddRange(await iterator.ReadNextAsync());
        }

        return organizations;
    }

    // The transfer service over the Cosmos DB services and the job store of the fixture, with the archives and the queue
    // in memory: one service for both organizations, as in a deployment.
    private sealed class Transfers
    {
        private readonly MemoryQueue _queue = new();

        public Transfers(IServiceProvider services)
        {
            Service = new ViewTransferService(
                services.GetRequiredService<IViewTransferStore>(),
                new MemoryArchives(),
                _queue,
                services.GetRequiredService<IAnnotationService>(),
                services.GetRequiredService<IConfigurationSchemaService>(),
                services.GetRequiredService<IConfigurationTemplateService>(),
                services.GetRequiredService<IConfigurationService>(),
                Options.Create(new ViewTransferOptions()),
                TimeProvider.System,
                NullLogger<ViewTransferService>.Instance);
        }

        public ViewTransferService Service { get; }

        public ViewTransferMessage LastMessage => _queue.Messages[^1];

        // Exports the view of the project in the folder format and runs the job to the end.
        public async Task<(ViewExport Job, byte[] Archive)> ExportAsync(string organization, string view, AuditActor actor)
        {
            var started = await Service.StartExportAsync(organization, Project, view, ViewArchiveFormat.Folder, null, actor);
            await Service.ProcessAsync(LastMessage);

            var (export, archive) = await Service.OpenExportArchiveAsync(organization, Project, view, started.Id);
            await using (archive)
            {
                using var buffer = new MemoryStream();
                await archive.CopyToAsync(buffer);
                return (export, buffer.ToArray());
            }
        }

        // Imports every kind of the archive into the view of the project and runs the job to the end.
        public async Task<ViewImport> ImportAsync(string organization, string view, byte[] archive, ViewImportExisting existing, AuditActor actor)
        {
            using var stream = new MemoryStream(archive);
            var started = await Service.StartImportAsync(organization, Project, view, stream, "export.zip", null, existing, false, false, actor);
            await Service.ProcessAsync(LastMessage);
            return (await Service.GetImportAsync(organization, Project, view, started.Id))!;
        }
    }

    private sealed class MemoryArchives : IViewArchiveStorage
    {
        private readonly Dictionary<string, byte[]> _archives = new(StringComparer.Ordinal);

        public async Task UploadAsync(string name, Stream content, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            _archives[name] = buffer.ToArray();
        }

        public Task<Stream?> OpenReadAsync(string name, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Stream?>(_archives.TryGetValue(name, out var archive) ? new MemoryStream(archive, writable: false) : null);
        }

        public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
        {
            _archives.Remove(name);
            return Task.CompletedTask;
        }

        public Task<int> DeleteOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class MemoryQueue : IViewTransferQueue
    {
        public List<ViewTransferMessage> Messages { get; } = [];

        public Task EnqueueAsync(ViewTransferMessage message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
