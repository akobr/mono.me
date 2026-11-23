using System.Globalization;
using _42.Platform.Storyteller.Backend.Core.UnitTests.Notifying;
using _42.Platform.Storyteller.Notifying;
using _42.Platform.Storyteller.Transferring;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring.TransferTestData;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

public class ViewTransferNotificationsTests
{
    private const string Id = "0123456789abcdef0123456789abcdef";
    private const string ArchiveName = "export-acme-main-default-20261001-080000Z.zip";

    private static readonly DateTimeOffset CompletedAt = Start.AddMinutes(3);
    private static readonly DateTimeOffset ExpiresAt = CompletedAt.AddHours(24);

    // An export of every kind that succeeded three minutes after Start.
    private static readonly ViewExport Exported = new()
    {
        Id = Id,
        Status = ViewTransferStatus.Succeeded,
        Format = ViewArchiveFormat.Folder,
        FileName = ArchiveName,
        CreatedAt = Start,
        CreatedBy = Actor,
        StartedAt = Start.AddMinutes(1),
        CompletedAt = CompletedAt,
        ExpiresAt = ExpiresAt,
        AnnotationCount = 12,
        SchemaCount = 3,
        TemplateCount = 2,
        ConfigurationCount = 40,
        VersionCount = 58,
        Size = 1_258_291,
    };

    // An import of "acme.zip" that succeeded at the same time, without a failed file.
    private static readonly ViewImport Imported = new()
    {
        Id = Id,
        Status = ViewTransferStatus.Succeeded,
        FileName = "acme.zip",
        CreatedAt = Start,
        CreatedBy = Actor,
        StartedAt = Start.AddMinutes(1),
        CompletedAt = CompletedAt,
        ExpiresAt = ExpiresAt,
        TotalCount = 16,
        ProcessedCount = 16,
        Summary = new ViewImportSummary { Created = 3, Updated = 2, Unchanged = 10, Skipped = 1 },
    };

    [Fact]
    public void ExportSucceeded_IsASuccessWithTheCountsAndTheSize_ThatExpiresWithTheJob()
    {
        Create(Exported).ShouldBe(new ViewInboxPush
        {
            Organization = "acme",
            Project = "main",
            View = "default",
            Kind = ViewInboxItemKind.Notification,
            Topic = "ViewExport",
            Level = ViewInboxItemLevel.Success,
            Title = "Export finished",
            Text = "12 annotations, 3 schemas, 2 templates, 40 configurations, 58 versions, 1.2 MB",
            MinimalRole = AccountRole.Administrator,
            CreatedBy = Actor,
            ExpiresAt = ExpiresAt,
            Job = new ViewInboxJob { Kind = ViewInboxJobKind.ViewExport, Id = Id, FileName = ArchiveName },
            Key = $"ViewExport.{Id}",
            CreatedAt = CompletedAt,
        });
    }

    [Fact]
    public void ExportFailed_IsAnErrorWithTheErrorOfTheJob_ThatLivesAsLongAsTheInboxKeepsItems()
    {
        var failed = Exported with
        {
            Status = ViewTransferStatus.Failed,
            Error = "The export failed: The view has no annotations.",
            AnnotationCount = null,
            SchemaCount = null,
            TemplateCount = null,
            ConfigurationCount = null,
            VersionCount = null,
            Size = null,
        };

        Create(failed).ShouldBe(new ViewInboxPush
        {
            Organization = "acme",
            Project = "main",
            View = "default",
            Kind = ViewInboxItemKind.Notification,
            Topic = "ViewExport",
            Level = ViewInboxItemLevel.Error,
            Title = "Export failed",
            Text = "The export failed: The view has no annotations.",
            MinimalRole = AccountRole.Administrator,
            CreatedBy = Actor,

            // not the day of the job: the publisher gives the retention of the inbox
            ExpiresAt = null,
            Job = new ViewInboxJob { Kind = ViewInboxJobKind.ViewExport, Id = Id, FileName = ArchiveName },
            Key = $"ViewExport.{Id}",
            CreatedAt = CompletedAt,
        });
    }

    [Fact]
    public void ImportSucceeded_IsASuccessWithTheFileAndEveryCount_ThatExpiresWithTheJob()
    {
        Create(Imported).ShouldBe(new ViewInboxPush
        {
            Organization = "acme",
            Project = "main",
            View = "default",
            Kind = ViewInboxItemKind.Notification,
            Topic = "ViewImport",
            Level = ViewInboxItemLevel.Success,
            Title = "Import finished",
            Text = "acme.zip: 3 created, 2 updated, 10 unchanged, 1 skipped, 0 failed",
            MinimalRole = AccountRole.Administrator,
            CreatedBy = Actor,
            ExpiresAt = ExpiresAt,
            Job = new ViewInboxJob { Kind = ViewInboxJobKind.ViewImport, Id = Id, FileName = "acme.zip" },
            Key = $"ViewImport.{Id}",
            CreatedAt = CompletedAt,
        });
    }

    [Theory]
    [InlineData(false, 0, ViewInboxItemLevel.Success, "Import finished")]
    [InlineData(true, 0, ViewInboxItemLevel.Success, "Check finished")]
    [InlineData(false, 1, ViewInboxItemLevel.Warning, "Import finished with failures")]
    [InlineData(true, 4, ViewInboxItemLevel.Warning, "Check found problems")]
    public void ImportSucceeded_IsNamedByItsFailedFilesAndTheDryRun(bool isDryRun, int failed, ViewInboxItemLevel level, string title)
    {
        var push = Create(Imported with { IsDryRun = isDryRun, Summary = Imported.Summary! with { Failed = failed } });

        push.Level.ShouldBe(level);
        push.Title.ShouldBe(title);

        // a result with failed files is still a result: it leads to the report of its job and expires with it
        push.ExpiresAt.ShouldBe(ExpiresAt);
        push.Text.ShouldEndWith($"1 skipped, {failed} failed");
    }

    [Theory]
    [InlineData(false, "Import failed")]
    [InlineData(true, "Check failed")]
    public void ImportFailed_IsAnErrorWithTheErrorOfTheJob_ThatLivesAsLongAsTheInboxKeepsItems(bool isDryRun, string title)
    {
        // the job stopped in the middle: the files that failed before do not make it a warning
        var failed = Imported with
        {
            Status = ViewTransferStatus.Failed,
            IsDryRun = isDryRun,
            Error = "The import failed: The uploaded archive no longer exists.",
            Summary = new ViewImportSummary { Created = 1, Failed = 2 },
        };

        Create(failed).ShouldBe(new ViewInboxPush
        {
            Organization = "acme",
            Project = "main",
            View = "default",
            Kind = ViewInboxItemKind.Notification,
            Topic = "ViewImport",
            Level = ViewInboxItemLevel.Error,
            Title = title,
            Text = "The import failed: The uploaded archive no longer exists.",
            MinimalRole = AccountRole.Administrator,
            CreatedBy = Actor,
            ExpiresAt = null,
            Job = new ViewInboxJob { Kind = ViewInboxJobKind.ViewImport, Id = Id, FileName = "acme.zip" },
            Key = $"ViewImport.{Id}",
            CreatedAt = CompletedAt,
        });
    }

    [Fact]
    public void ImportText_WithoutAFileName_IsOnlyTheCounts()
    {
        var push = Create(Imported with { FileName = null });

        push.Text.ShouldBe("3 created, 2 updated, 10 unchanged, 1 skipped, 0 failed");
        push.Job.ShouldBe(new ViewInboxJob { Kind = ViewInboxJobKind.ViewImport, Id = Id });
    }

    [Fact]
    public void ImportText_OfADryRun_NamesWhatARealRunWouldWrite()
    {
        var push = Create(Imported with { IsDryRun = true, Summary = new ViewImportSummary { Created = 1, Updated = 1, Skipped = 2, Failed = 5 } });

        push.Text.ShouldBe("acme.zip: 1 to create, 1 to update, 0 unchanged, 2 skipped, 5 failed");
    }

    [Fact]
    public void ImportText_WithoutASummary_HasNoCounts()
    {
        Create(Imported with { Summary = null }).Text.ShouldBe("acme.zip");
        Create(Imported with { Summary = null, FileName = null }).Text.ShouldBeNull();
        Create(Imported with { Summary = null }).Level.ShouldBe(ViewInboxItemLevel.Success);
    }

    [Fact]
    public void ExportText_NamesOnlyTheKindsThatWereExported()
    {
        // a kind that was left out has no count; one that was exported and found nothing has zero
        var push = Create(Exported with { SchemaCount = null, TemplateCount = 0, ConfigurationCount = null, VersionCount = 0, Size = 2048 });

        push.Text.ShouldBe("12 annotations, 0 templates, 0 versions, 2.0 KB");
    }

    [Theory]
    [InlineData(0, "0 annotations, 0 schemas, 0 templates, 0 configurations, 0 versions")]
    [InlineData(1, "1 annotation, 1 schema, 1 template, 1 configuration, 1 version")]
    [InlineData(2, "2 annotations, 2 schemas, 2 templates, 2 configurations, 2 versions")]
    [InlineData(1234567, "1234567 annotations, 1234567 schemas, 1234567 templates, 1234567 configurations, 1234567 versions")]
    public void ExportText_CountsInTheSingularAndThePlural(int count, string expected)
    {
        var push = Create(Exported with
        {
            AnnotationCount = count,
            SchemaCount = count,
            TemplateCount = count,
            ConfigurationCount = count,
            VersionCount = count,
            Size = null,
        });

        push.Text.ShouldBe(expected);
    }

    [Fact]
    public void ExportText_NamesTheVersionsOnlyWhenTheJobCountedThem()
    {
        Create(Exported with { VersionCount = null }).Text.ShouldBe("12 annotations, 3 schemas, 2 templates, 40 configurations, 1.2 MB");
    }

    [Fact]
    public void ExportText_OfAnnotationsAlone_NamesNoVersions()
    {
        // the job always counts the versions; without a kind that has versions the count is zero and says nothing
        var push = Create(Exported with { SchemaCount = null, TemplateCount = null, ConfigurationCount = null, VersionCount = 0, Size = 512 });

        push.Text.ShouldBe("12 annotations, 512 B");
    }

    [Fact]
    public void ExportText_WithoutCountsAndSize_IsMissing()
    {
        var bare = Exported with
        {
            AnnotationCount = null,
            SchemaCount = null,
            TemplateCount = null,
            ConfigurationCount = null,
            VersionCount = null,
        };

        Create(bare).Text.ShouldBe("1.2 MB");
        Create(bare with { Size = null }).Text.ShouldBeNull();
        Create(bare with { Size = null }).Title.ShouldBe("Export finished");
    }

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1L, "1 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1.0 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1587L, "1.5 KB")]
    [InlineData(1588L, "1.6 KB")]
    [InlineData(1_048_063L, "1023.5 KB")]

    // rounded before the unit is chosen: never 1024.0 KB
    [InlineData(1_048_575L, "1.0 MB")]
    [InlineData(1_048_576L, "1.0 MB")]
    [InlineData(1_258_291L, "1.2 MB")]
    [InlineData(20L * 1024 * 1024, "20.0 MB")]
    [InlineData(1_073_741_823L, "1.0 GB")]
    [InlineData(5L * 1024 * 1024 * 1024 / 2, "2.5 GB")]

    // no unit above GB
    [InlineData(1024L * 1024 * 1024 * 1024 * 3, "3072.0 GB")]
    public void ExportText_EndsWithAHumanSize(long size, string expected)
    {
        var push = Create(Exported with
        {
            AnnotationCount = null,
            SchemaCount = null,
            TemplateCount = null,
            ConfigurationCount = null,
            VersionCount = null,
            Size = size,
        });

        push.Text.ShouldBe(expected);
    }

    [Theory]
    [InlineData("cs-CZ")]
    [InlineData("fa-IR")]
    public void Texts_AreWrittenWithTheInvariantCulture(string culture)
    {
        var before = CultureInfo.CurrentCulture;

        try
        {
            // cultures with a decimal comma, or with separators of their own
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

            Create(Exported with { AnnotationCount = 1234567 }).Text
                .ShouldBe("1234567 annotations, 3 schemas, 2 templates, 40 configurations, 58 versions, 1.2 MB");
            Create(Exported with { Size = 512 }).Text.ShouldEndWith(", 512 B");
            Create(Imported with { Summary = new ViewImportSummary { Created = 1234567, Unchanged = 10 } }).Text
                .ShouldBe("acme.zip: 1234567 created, 0 updated, 10 unchanged, 0 skipped, 0 failed");
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void Push_IsOfTheViewAndTheActorOfTheJob_AlsoAMachineAndAnActor()
    {
        var pipeline = AuditActor.Machine("machine-1", "Nightly export");
        var viaActor = Actor with { Via = "mcp" };

        var export = ViewTransferNotifications.Create(Record(Exported with { CreatedBy = pipeline }) with { Organization = "globex", Project = "billing", View = "staging" });
        var import = ViewTransferNotifications.Create(Record(Imported with { CreatedBy = viaActor }) with { Organization = "globex", Project = "billing", View = "staging" });

        (export.Organization, export.Project, export.View).ShouldBe(("globex", "billing", "staging"));
        (import.Organization, import.Project, import.View).ShouldBe(("globex", "billing", "staging"));
        export.CreatedBy.ShouldBe(pipeline);
        import.CreatedBy.ShouldBe(viaActor);
    }

    [Fact]
    public void Push_OfAnExportAndAnImportWithOneId_HaveTheirOwnKeys()
    {
        Create(Exported).Key.ShouldBe($"ViewExport.{Id}");
        Create(Imported).Key.ShouldBe($"ViewImport.{Id}");
    }

    [Fact]
    public void Push_OfAnEndWithoutItsTime_HasTheLastTimeOfTheJob()
    {
        // every writer of an end sets CompletedAt; a record without it is still reported, at a time that is the same
        // for every push of the job
        Create(Exported with { CompletedAt = null }).CreatedAt.ShouldBe(Start.AddMinutes(1));
        Create(Exported with { CompletedAt = null, StartedAt = null }).CreatedAt.ShouldBe(Start);
        Create(Imported with { CompletedAt = null }).CreatedAt.ShouldBe(Start.AddMinutes(1));
        Create(Imported with { CompletedAt = null, StartedAt = null }).CreatedAt.ShouldBe(Start);
    }

    [Theory]
    [InlineData(ViewTransferStatus.Queued)]
    [InlineData(ViewTransferStatus.Running)]
    public void Create_RefusesAJobThatHasNotEnded(ViewTransferStatus status)
    {
        Should.Throw<ArgumentException>(() => Create(Exported with { Status = status })).Message.ShouldContain($"has not ended; it is {status}");
        Should.Throw<ArgumentException>(() => Create(Imported with { Status = status })).Message.ShouldContain($"has not ended; it is {status}");
    }

    [Fact]
    public void Create_RefusesARecordWithoutTheModelOfItsKind()
    {
        // such a record counts as failed (ViewTransferRecord.Status), but it has no actor and no time to report
        var export = Record(Exported) with { Export = null };
        var import = Record(Imported) with { Import = null };
        var mixed = Record(Imported) with { Kind = ViewTransferKind.Export };

        export.Status.ShouldBe(ViewTransferStatus.Failed);
        Should.Throw<ArgumentException>(() => ViewTransferNotifications.Create(export)).Message.ShouldContain("has no export");
        Should.Throw<ArgumentException>(() => ViewTransferNotifications.Create(import)).Message.ShouldContain("has no import");
        Should.Throw<ArgumentException>(() => ViewTransferNotifications.Create(mixed)).Message.ShouldContain("has no export");
    }

    // What the table promises of the lifetimes is kept by the publisher: a result expires with its job, a failure after
    // the retention of the inbox, and every push of one job is one item.
    [Theory]
    [InlineData(ViewTransferKind.Export, ViewTransferStatus.Succeeded, 1)]
    [InlineData(ViewTransferKind.Export, ViewTransferStatus.Failed, 30)]
    [InlineData(ViewTransferKind.Import, ViewTransferStatus.Succeeded, 1)]
    [InlineData(ViewTransferKind.Import, ViewTransferStatus.Failed, 30)]
    public async Task Push_IsStoredByThePublisherAsOneItemPerJob(ViewTransferKind kind, ViewTransferStatus status, int days)
    {
        var time = new ManualTimeProvider(CompletedAt);
        var store = new InMemoryViewInboxStore();
        var publisher = new ViewInboxPublisher(store, Options.Create(new ViewInboxOptions()), time, NullLogger<ViewInboxPublisher>.Instance);
        var record = kind == ViewTransferKind.Export
            ? Record(Exported with { Status = status, Error = "The export failed: boom" })
            : Record(Imported with { Status = status, Error = "The import failed: boom" });

        var item = await publisher.PushAsync(ViewTransferNotifications.Create(record), TestContext.Current.CancellationToken);

        // the job is read again later, by a redelivered message: the same item, and nothing is written twice
        time.Now = CompletedAt.AddMinutes(10);
        var again = await publisher.PushAsync(ViewTransferNotifications.Create(record), TestContext.Current.CancellationToken);

        item.Id.ShouldBe(ViewInboxIds.ForKey(CompletedAt, $"View{kind}.{Id}"));
        item.CreatedAt.ShouldBe(CompletedAt);
        item.ExpiresAt.ShouldBe(CompletedAt.AddDays(days));
        again.ShouldBe(item);
        store.Writes.ShouldHaveSingleItem().TimeToLive.ShouldBe(TimeSpan.FromDays(days));
    }

    private static ViewInboxPush Create(ViewExport export) => ViewTransferNotifications.Create(Record(export));

    private static ViewInboxPush Create(ViewImport import) => ViewTransferNotifications.Create(Record(import));

    // The job of "acme.main.default" as the store keeps it.
    private static ViewTransferRecord Record(ViewExport export)
    {
        return new ViewTransferRecord { Organization = "acme", Project = "main", View = "default", Kind = ViewTransferKind.Export, Id = export.Id, Export = export };
    }

    private static ViewTransferRecord Record(ViewImport import)
    {
        return new ViewTransferRecord { Organization = "acme", Project = "main", View = "default", Kind = ViewTransferKind.Import, Id = import.Id, Import = import };
    }
}
