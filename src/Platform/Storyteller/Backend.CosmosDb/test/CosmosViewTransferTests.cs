#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Transferring;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

public class CosmosViewTransferTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Project = "transfer-tests";
    private const string View = Constants.DefaultViewName;
    private static readonly AuditActor Author = AuditActor.Account("importer", "Importer");

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    private IConfigurationService Configurations => Context.Services.GetRequiredService<IConfigurationService>();

    private IConfigurationTemplateService Templates => Context.Services.GetRequiredService<IConfigurationTemplateService>();

    private IConfigurationSchemaService Schemas => Context.Services.GetRequiredService<IConfigurationSchemaService>();

    [Fact]
    public async Task Histories_HoldTheOwnDocumentsAndTheirVersions()
    {
        const string project = "transfer-histories";
        await CreateAsync(project, View, "rst.hist-one", "unt.hist-one.unit");
        var key = Key("rst.hist-one", project);

        await Configurations.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{"a":1}"""), AuditActor.Account("first", "First"));
        await Configurations.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{"a":2}"""), AuditActor.Account("second", "Second"));

        // the unit has no document of its own; reading it caches the inherited one in an empty item
        (await Configurations.GetRawConfigurationAsync(Key("unt.hist-one.unit", project)))!.Content["a"]!.Value<int>().Should().Be(2);

        var histories = await Configurations.GetConfigurationHistoriesAsync(TestConstants.Organization, project, View);

        histories.Select(history => history.AnnotationKey).Should().Equal("rst.hist-one");
        var history = histories[0];
        history.Versions.Select(version => version.Version).Should().Equal(1UL, 2UL);
        history.Versions[0].Content["a"]!.Value<int>().Should().Be(1);
        history.Versions[0].Author.Should().Be("First");
        history.Versions[0].Actor.Should().Be(AuditActor.Account("first", "First"));
        history.Current.Should().BeSameAs(history.Versions[1]);
        history.Current!.Content["a"]!.Value<int>().Should().Be(2);
        history.Current.Author.Should().Be("Second");
        history.Current.CreatedAt.Should().BeOnOrAfter(history.Versions[0].CreatedAt);
    }

    [Fact]
    public async Task Histories_KeepClearedConfigurationsWithoutACurrentDocument()
    {
        const string project = "transfer-cleared";
        await CreateAsync(project, View, "rst.cleared");
        var key = Key("rst.cleared", project);

        await Configurations.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{"a":1}"""), Author);
        await Configurations.ClearConfigurationAsync(key);

        var history = (await Configurations.GetConfigurationHistoriesAsync(TestConstants.Organization, project, View)).Single();

        history.Current.Should().BeNull();
        history.Versions.Select(version => version.Version).Should().Equal(1UL);
    }

    [Fact]
    public async Task Histories_LeaveOutOtherViewsAndDeletedAnnotations()
    {
        const string project = "transfer-scope";
        await CreateAsync(project, View, "rst.kept", "rst.deleted");
        await CreateAsync(project, "other", "rst.other-view");
        await CreateAsync("transfer-scope-2", View, "rst.other-project");

        await Configurations.CreateOrUpdateConfigurationAsync(Key("rst.kept", project), JObject.Parse("""{"a":1}"""), Author);
        await Configurations.CreateOrUpdateConfigurationAsync(Key("rst.deleted", project), JObject.Parse("""{"a":1}"""), Author);
        await Configurations.CreateOrUpdateConfigurationAsync(Key("rst.deleted", project), JObject.Parse("""{"a":2}"""), Author);
        await Configurations.CreateOrUpdateConfigurationAsync(Key("rst.other-view", project, "other"), JObject.Parse("""{"a":1}"""), Author);
        await Configurations.CreateOrUpdateConfigurationAsync(Key("rst.other-project", "transfer-scope-2"), JObject.Parse("""{"a":1}"""), Author);
        await Annotations.DeleteAnnotationAsync(Key("rst.deleted", project));

        var histories = await Configurations.GetConfigurationHistoriesAsync(TestConstants.Organization, project, View);

        histories.Select(history => history.AnnotationKey).Should().Equal("rst.kept");
    }

    [Fact]
    public async Task TemplateHistories_HoldEveryTypeWithItsVersionsAndDeletedTemplates()
    {
        const string project = "transfer-templates";
        const string organization = TestConstants.Organization;
        var first = AuditActor.Account("first", "First");
        var machine = AuditActor.Machine("deployer");

        await Templates.CreateOrUpdateTemplateAsync(organization, project, View, AnnotationTypeCodes.Execution, JObject.Parse("""{"a":1}"""), first);
        await Templates.CreateOrUpdateTemplateAsync(organization, project, View, AnnotationTypeCodes.Execution, JObject.Parse("""{"b":2}"""), machine);
        await Templates.CreateOrUpdateTemplateAsync(organization, project, View, AnnotationTypeCodes.Unit, JObject.Parse("""{"u":1}"""), first);
        await Templates.DeleteTemplateAsync(organization, project, View, AnnotationTypeCodes.Unit);

        // an empty document is not a version
        await Templates.CreateOrUpdateTemplateAsync(organization, project, View, AnnotationTypeCodes.Usage, new JObject(), first);
        await Templates.CreateOrUpdateTemplateAsync(organization, project, View, AnnotationTypeCodes.Usage, JObject.Parse("""{"g":1}"""), first);
        await Templates.CreateOrUpdateTemplateAsync(organization, project, View, AnnotationTypeCodes.Context, new JObject(), first);

        // other views and projects are left out
        await Templates.CreateOrUpdateTemplateAsync(organization, project, "other", AnnotationTypeCodes.Responsibility, JObject.Parse("""{"o":1}"""), first);
        await Templates.CreateOrUpdateTemplateAsync(organization, "transfer-templates-2", View, AnnotationTypeCodes.Responsibility, JObject.Parse("""{"o":1}"""), first);

        var histories = await Templates.GetTemplateHistoriesAsync(organization, project, View);

        histories.Select(history => history.AnnotationType).Should().Equal(AnnotationTypeCodes.Execution, AnnotationTypeCodes.Unit, AnnotationTypeCodes.Usage);

        var execution = histories[0];
        execution.Versions.Select(version => version.Version).Should().Equal(1UL, 2UL);
        execution.Versions[0].Author.Should().Be("First");
        execution.Versions[0].Actor.Should().Be(first);
        JToken.DeepEquals(execution.Versions[0].Content, JObject.Parse("""{"a":1}""")).Should().BeTrue();
        execution.Current.Should().BeSameAs(execution.Versions[1]);
        JToken.DeepEquals(execution.Current!.Content, JObject.Parse("""{"a":1,"b":2}""")).Should().BeTrue();
        execution.Current.Author.Should().Be("deployer");
        execution.Current.Actor.Should().Be(machine);
        execution.Current.CreatedAt.Should().BeOnOrAfter(execution.Versions[0].CreatedAt);

        // a deleted template has its versions but no current document
        var unit = histories[1];
        unit.Current.Should().BeNull();
        unit.Versions.Select(version => version.Version).Should().Equal(1UL);
        JToken.DeepEquals(unit.Versions[0].Content, JObject.Parse("""{"u":1}""")).Should().BeTrue();

        var usage = histories[2];
        usage.Versions.Select(version => version.Version).Should().Equal(2UL);
        usage.Current.Should().BeSameAs(usage.Versions[0]);
    }

    [Fact]
    public async Task SchemaHistories_HoldEveryKindWithItsVersionsAndDeletedSchemas()
    {
        const string project = "transfer-schemas";
        const string organization = TestConstants.Organization;
        var author = AuditActor.Account("schemer", "Schemer");
        await CreateAsync(project, View, "rst.payments", "sbt.northwind");

        await Schemas.SetSchemaAsync(organization, project, View, AnnotationTypeCodes.Execution, StringProperty("a"), author, false);
        await Schemas.SetSchemaAsync(organization, project, View, AnnotationTypeCodes.Execution, StringProperty("b"), author, false);
        await Schemas.SetSchemaAsync(organization, project, View, AnnotationTypeCodes.Unit, StringProperty("u"), author, false);
        await Schemas.DeleteSchemaAsync(organization, project, View, AnnotationTypeCodes.Unit);
        await Schemas.SetDescendantTypeSchemaAsync(organization, project, View, "sbt.northwind", AnnotationTypeCodes.Execution, StringProperty("d"), author, false);
        await Schemas.SetAnnotationSchemaAsync(organization, project, View, "sbt.northwind", StringProperty("n"), author, false);
        await Schemas.SetAnnotationSchemaAsync(organization, project, View, "rst.payments", StringProperty("p"), author, false);
        await Schemas.DeleteAnnotationSchemaAsync(organization, project, View, "rst.payments");

        // an empty document is not a version
        await Schemas.SetSchemaAsync(organization, project, View, AnnotationTypeCodes.Usage, new JObject(), author, false);
        await Schemas.SetSchemaAsync(organization, project, View, AnnotationTypeCodes.Usage, StringProperty("g"), author, false);
        await Schemas.SetSchemaAsync(organization, project, View, AnnotationTypeCodes.Context, new JObject(), author, false);

        // other views are left out
        await CreateAsync(project, "other", "rst.payments");
        await Schemas.SetSchemaAsync(organization, project, "other", AnnotationTypeCodes.Responsibility, StringProperty("o"), author, false);
        await Schemas.SetAnnotationSchemaAsync(organization, project, "other", "rst.payments", StringProperty("o"), author, false);

        var histories = await Schemas.GetSchemaHistoriesAsync(organization, project, View);

        histories.Select(history => (history.Kind, history.AnnotationType, history.AnnotationKey)).Should().Equal(
            (ViewDocumentKind.TypeSchema, AnnotationTypeCodes.Execution, null),
            (ViewDocumentKind.TypeSchema, AnnotationTypeCodes.Unit, null),
            (ViewDocumentKind.TypeSchema, AnnotationTypeCodes.Usage, null),
            (ViewDocumentKind.DescendantTypeSchema, AnnotationTypeCodes.Execution, "sbt.northwind"),
            (ViewDocumentKind.AnnotationSchema, null, "rst.payments"),
            (ViewDocumentKind.AnnotationSchema, null, "sbt.northwind"));

        var execution = histories[0];
        execution.Versions.Select(version => version.Version).Should().Equal(1UL, 2UL);
        JToken.DeepEquals(execution.Versions[0].Content, StringProperty("a")).Should().BeTrue();
        execution.Current.Should().BeSameAs(execution.Versions[1]);
        JToken.DeepEquals(execution.Current!.Content, StringProperty("b")).Should().BeTrue();
        execution.Current.Author.Should().Be("Schemer");
        execution.Current.Actor.Should().Be(author);

        // a deleted schema has its versions but no current document
        histories[1].Current.Should().BeNull();
        histories[1].Versions.Select(version => version.Version).Should().Equal(1UL);
        histories[4].Current.Should().BeNull();
        histories[4].Versions.Select(version => version.Version).Should().Equal(1UL);
        JToken.DeepEquals(histories[4].Versions[0].Content, StringProperty("p")).Should().BeTrue();

        histories[2].Versions.Select(version => version.Version).Should().Equal(2UL);
        histories[2].Current.Should().BeSameAs(histories[2].Versions[0]);
        JToken.DeepEquals(histories[3].Current!.Content, StringProperty("d")).Should().BeTrue();
        JToken.DeepEquals(histories[5].Current!.Content, StringProperty("n")).Should().BeTrue();
    }

    [Fact]
    public async Task SchemaHistories_LeaveOutSchemasOfDeletedAndMissingAnnotations()
    {
        const string project = "transfer-schema-scope";
        const string organization = TestConstants.Organization;
        var author = AuditActor.Account("schemer", "Schemer");
        await CreateAsync(project, View, "rst.kept", "rst.gone", "cnt.gone.child");
        await CreateAsync(project, "other", "rst.elsewhere");
        await Annotations.CreateAnnotationAsync(
            organization,
            AnnotationFactory.CreateDefault(AnnotationKey.Parse("rst.off"), project, View) with { IsDisabled = true },
            AnnotationServiceTestExtensions.TestActor);

        await Schemas.SetSchemaAsync(organization, project, View, AnnotationTypeCodes.Context, StringProperty("t"), author, false);
        await Schemas.SetAnnotationSchemaAsync(organization, project, View, "rst.kept", StringProperty("k"), author, false);
        await Schemas.SetAnnotationSchemaAsync(organization, project, View, "rst.off", StringProperty("o"), author, false);

        // deleted: rst.gone itself, sbt.gone with its context
        await Schemas.SetAnnotationSchemaAsync(organization, project, View, "rst.gone", StringProperty("g"), author, false);
        await Schemas.SetDescendantTypeSchemaAsync(organization, project, View, "rst.gone", AnnotationTypeCodes.Unit, StringProperty("g"), author, false);
        await Schemas.SetDescendantTypeSchemaAsync(organization, project, View, "sbt.gone", AnnotationTypeCodes.Context, StringProperty("g"), author, false);
        await Schemas.SetAnnotationSchemaAsync(organization, project, View, "cnt.gone.child", StringProperty("g"), author, false);

        // missing: never created, or only in another view
        await Schemas.SetAnnotationSchemaAsync(organization, project, View, "rst.never", StringProperty("m"), author, false);
        await Schemas.SetDescendantTypeSchemaAsync(organization, project, View, "sbt.never", AnnotationTypeCodes.Execution, StringProperty("m"), author, false);
        await Schemas.SetAnnotationSchemaAsync(organization, project, View, "rst.elsewhere", StringProperty("m"), author, false);

        await Annotations.DeleteAnnotationAsync(Key("rst.gone", project));
        await Annotations.DeleteAnnotationAsync(Key("sbt.gone", project));

        var histories = await Schemas.GetSchemaHistoriesAsync(organization, project, View);

        // a disabled annotation is not deleted: its schema stays
        histories.Select(history => (history.Kind, history.AnnotationType, history.AnnotationKey)).Should().Equal(
            (ViewDocumentKind.TypeSchema, AnnotationTypeCodes.Context, null),
            (ViewDocumentKind.AnnotationSchema, null, "rst.kept"),
            (ViewDocumentKind.AnnotationSchema, null, "rst.off"));
    }

    [Fact]
    public async Task ViewAnnotations_HoldEveryTypeAndStatusWithoutDeletedOnes()
    {
        const string project = "transfer-annotations";
        const string organization = TestConstants.Organization;
        await CreateAsync(project, View, "unt.va-kept.unit", "cnt.va-sub.va-ctx", "unt.va-gone.unit");
        await CreateAsync(project, "other", "rst.va-other");
        await Annotations.CreateAnnotationAsync(
            organization,
            AnnotationFactory.CreateDefault(AnnotationKey.Parse("rst.va-off"), project, View) with { IsDisabled = true, Title = "Off" },
            Author);
        await Annotations.CreateAnnotationAsync(
            organization,
            AnnotationFactory.CreateDefault(AnnotationKey.Parse("sbt.va-expired"), project, View) with { ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) },
            Author);
        await Annotations.DeleteAnnotationAsync(Key("rst.va-gone", project));

        var annotations = await Annotations.GetViewAnnotationsAsync(organization, project, View);

        annotations.Select(annotation => annotation.AnnotationKey).Should().BeEquivalentTo(
            "rst.va-kept",
            "unt.va-kept.unit",
            "sbt.va-sub",
            "cnt.va-sub.va-ctx",
            "rst.va-off",
            "sbt.va-expired");
        annotations.Should().OnlyContain(annotation => annotation.Created != null && annotation.Modified != null && annotation.Deletion == null);

        var off = annotations.Single(annotation => annotation.AnnotationKey == "rst.va-off");
        off.Should().BeOfType<Responsibility>();
        off.IsDisabled.Should().BeTrue();
        off.Title.Should().Be("Off");
        off.Created!.By.Should().Be(Author);
        annotations.Single(annotation => annotation.AnnotationKey == "unt.va-kept.unit").Should().BeOfType<Unit>();
    }

    [Fact]
    public async Task ActiveAnnotationKeys_LeaveOutDeletedAndMissingOnes()
    {
        const string project = "transfer-active-keys";
        const string organization = TestConstants.Organization;
        await CreateAsync(project, View, "unt.ak-kept.ak-unit", "rst.ak-gone");
        await CreateAsync(project, "other", "sbt.ak-elsewhere");
        await Annotations.CreateAnnotationAsync(
            organization,
            AnnotationFactory.CreateDefault(AnnotationKey.Parse("rst.ak-off"), project, View) with { IsDisabled = true },
            Author);
        await Annotations.DeleteAnnotationAsync(Key("rst.ak-gone", project));

        var keys = await Annotations.FindActiveAnnotationKeysAsync(
            organization,
            project,
            View,
            new[] { "rst.ak-kept", "unt.ak-kept.ak-unit", "rst.ak-gone", "rst.ak-off", "rst.ak-never", "sbt.ak-elsewhere" }.Select(AnnotationKey.Parse).ToList());

        // a disabled annotation is not deleted
        keys.Should().BeEquivalentTo("rst.ak-kept", "unt.ak-kept.ak-unit", "rst.ak-off");
        (await Annotations.FindActiveAnnotationKeysAsync(organization, project, View, [])).Should().BeEmpty();
    }

    [Fact]
    public async Task SchemaKeys_HoldTheSchemasOfTheViewWithADocument()
    {
        const string project = "transfer-schema-keys";
        const string organization = TestConstants.Organization;
        await CreateAsync(project, View, "rst.sk-pay", "sbt.sk-north");

        await Schemas.SetSchemaAsync(organization, project, View, AnnotationTypeCodes.Execution, StringProperty("a"), Author, false);
        await Schemas.SetAnnotationSchemaAsync(organization, project, View, "rst.sk-pay", StringProperty("p"), Author, false);
        await Schemas.SetDescendantTypeSchemaAsync(organization, project, View, "sbt.sk-north", AnnotationTypeCodes.Execution, StringProperty("d"), Author, false);

        // deleted, empty, or in another view
        await Schemas.SetSchemaAsync(organization, project, View, AnnotationTypeCodes.Unit, StringProperty("u"), Author, false);
        await Schemas.DeleteSchemaAsync(organization, project, View, AnnotationTypeCodes.Unit);
        await Schemas.SetSchemaAsync(organization, project, View, AnnotationTypeCodes.Usage, new JObject(), Author, false);
        await Schemas.SetSchemaAsync(organization, project, "other", AnnotationTypeCodes.Responsibility, StringProperty("o"), Author, false);

        var keys = await Schemas.GetSchemaKeysAsync(organization, project, View);

        keys.Should().BeEquivalentTo(
        [
            SchemaKey.ForType(AnnotationTypeCodes.Execution),
            SchemaKey.ForAnnotation("rst.sk-pay"),
            SchemaKey.ForDescendantType("sbt.sk-north", AnnotationTypeCodes.Execution),
        ]);
    }

    [Fact]
    public async Task Import_CreatesThenReplacesThenLeavesAnEqualDocument()
    {
        await CreateAsync(Project, View, "rst.import-replace");
        var key = Key("rst.import-replace");

        var created = await ImportAsync(key, JObject.Parse("""{"a":1,"list":[1,2]}"""), Author);
        created.Should().Be(new ViewImportResult(ViewImportOutcome.Created, 1));

        // a merge would keep "a" and unite the lists; an import replaces the document
        var updated = await ImportAsync(key, JObject.Parse("""{"list":[3],"b":2}"""), Author);
        updated.Should().Be(new ViewImportResult(ViewImportOutcome.Updated, 2));

        var stored = await Configurations.GetConfigurationVersionContentAsync(key, 2);
        JToken.DeepEquals(stored!.Content, JObject.Parse("""{"list":[3],"b":2}""")).Should().BeTrue();
        stored.Modified.By.Should().Be(Author);

        var unchanged = await ImportAsync(key, JObject.Parse("""{"b":2,"list":[3]}"""), Author);
        unchanged.Should().Be(new ViewImportResult(ViewImportOutcome.Unchanged, 2));

        (await Configurations.GetConfigurationVersionsAsync(key)).Select(version => version.Version).Should().Equal(1u, 2u);
    }

    [Fact]
    public async Task Import_AppliesTheDirectivesToTheDocumentAlone()
    {
        await CreateAsync(Project, View, "rst.import-directives");
        var key = Key("rst.import-directives");

        var result = await ImportAsync(
            key,
            JObject.Parse("""{"a":1,"b":2,"$remove":["$.b"],"$patch":[{"op":"add","path":"/c","value":3}]}"""),
            Author);

        result.Outcome.Should().Be(ViewImportOutcome.Created);
        var stored = await Configurations.GetConfigurationVersionContentAsync(key, 1);
        JToken.DeepEquals(stored!.Content, JObject.Parse("""{"a":1,"c":3}""")).Should().BeTrue();

        var broken = await ImportAsync(key, JObject.Parse("""{"$patch":[{"op":"remove","path":"/missing"}]}"""), Author);
        broken.Outcome.Should().Be(ViewImportOutcome.Failed);
        broken.ErrorCode.Should().Be(ErrorCodes.PatchInvalid);

        // a $remove that is not a JSONPath, or that takes the whole document, fails the file, not the job
        var brokenRemove = await ImportAsync(key, JObject.Parse("""{"a":2,"$remove":["$.a[?(@.x"]}"""), Author);
        var wholeRemove = await ImportAsync(key, JObject.Parse("""{"a":2,"$remove":["$"]}"""), Author);
        brokenRemove.Outcome.Should().Be(ViewImportOutcome.Failed);
        brokenRemove.ErrorCode.Should().Be(ErrorCodes.PatchInvalid);
        wholeRemove.Outcome.Should().Be(ViewImportOutcome.Failed);
        wholeRemove.ErrorCode.Should().Be(ErrorCodes.PatchInvalid);
        (await Configurations.GetConfigurationVersionsAsync(key)).Select(version => version.Version).Should().Equal(1u);
    }

    [Fact]
    public async Task Transfer_ReportsABrokenRemoveDirectiveOnItsFileAndImportsTheRest()
    {
        const string project = "transfer-broken-remove";
        var transfers = new Transfers(Context.Services);
        var archive = Zip(
            ("annotations/rst.broken-remove.json", """{"Title":"Broken remove"}"""),
            ("annotations/rst.kept-remove.json", "{}"),
            ("templates/exe.template.json", """{"a":1,"$remove":["$["]}"""),
            ("templates/unt.template.json", """{"u":1,"v":2,"$remove":["$.v"]}"""),
            ("configurations/rst.broken-remove.conf.json", """{"b":1,"$remove":["$..[?(@.x"]}"""),
            ("configurations/rst.kept-remove.conf.json", """{"c":1}"""));

        var imported = await transfers.ImportAsync(project, View, archive);

        imported.Status.Should().Be(ViewTransferStatus.Succeeded);
        imported.Items!.Select(item => (item.Path, item.Outcome, item.ErrorCode)).Should().Equal(
            ("annotations/rst.broken-remove.json", ViewImportOutcome.Created, null),
            ("annotations/rst.kept-remove.json", ViewImportOutcome.Created, null),
            ("templates/exe.template.json", ViewImportOutcome.Failed, ErrorCodes.PatchInvalid),
            ("templates/unt.template.json", ViewImportOutcome.Created, null),
            ("configurations/rst.broken-remove.conf.json", ViewImportOutcome.Failed, ErrorCodes.PatchInvalid),
            ("configurations/rst.kept-remove.conf.json", ViewImportOutcome.Created, null));
        imported.Summary.Should().Be(new ViewImportSummary { Created = 4, Failed = 2 });
        JToken.DeepEquals((await Templates.GetTemplateAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Unit))!.Content, JObject.Parse("""{"u":1}""")).Should().BeTrue();
        (await Configurations.HasConfigurationContentAsync(Key("rst.kept-remove", project))).Should().BeTrue();
    }

    [Fact]
    public async Task Import_SkipsMissingDeletedAndEmpty()
    {
        await CreateAsync(Project, View, "rst.import-deleted", "rst.import-empty");
        await Annotations.DeleteAnnotationAsync(Key("rst.import-deleted"));

        var missing = await ImportAsync(Key("rst.import-missing"), JObject.Parse("""{"a":1}"""), Author);
        missing.Outcome.Should().Be(ViewImportOutcome.Skipped);
        missing.ErrorCode.Should().Be(ErrorCodes.AnnotationNotFound);

        var deleted = await ImportAsync(Key("rst.import-deleted"), JObject.Parse("""{"a":1}"""), Author);
        deleted.Outcome.Should().Be(ViewImportOutcome.Skipped);
        deleted.ErrorCode.Should().Be(ErrorCodes.AnnotationDeleted);

        var empty = await ImportAsync(Key("rst.import-empty"), new JObject(), Author);
        empty.Outcome.Should().Be(ViewImportOutcome.Skipped);
        empty.ErrorCode.Should().Be(ErrorCodes.EmptyDocument);

        (await Configurations.HasConfigurationContentAsync(Key("rst.import-empty"))).Should().BeFalse();
    }

    [Fact]
    public async Task Import_DryRunWritesNothing()
    {
        await CreateAsync(Project, View, "rst.import-dry");
        var key = Key("rst.import-dry");

        var result = await ImportAsync(key, JObject.Parse("""{"a":1}"""), Author, dryRun: true);

        result.Should().Be(new ViewImportResult(ViewImportOutcome.Created));
        (await Configurations.HasConfigurationContentAsync(key)).Should().BeFalse();
    }

    [Fact]
    public async Task Import_IsValidatedUnlessForced()
    {
        const string project = "transfer-schema";
        await CreateAsync(project, View, "rst.import-schema");
        var key = Key("rst.import-schema", project);
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        await schemas.SetSchemaAsync(
            TestConstants.Organization,
            project,
            View,
            AnnotationTypeCodes.Responsibility,
            JObject.Parse("""{"type":"object","properties":{"a":{"type":"number"}}}"""),
            AuditActor.Account("test"),
            false);

        var rejected = await ImportAsync(key, JObject.Parse("""{"a":"text"}"""), Author);
        rejected.Outcome.Should().Be(ViewImportOutcome.Failed);
        rejected.ErrorCode.Should().Be(ErrorCodes.SchemaViolation);
        rejected.Message.Should().StartWith("The document violates its schema: ");

        var dryRun = await ImportAsync(key, JObject.Parse("""{"a":"text"}"""), Author, dryRun: true);
        dryRun.Outcome.Should().Be(ViewImportOutcome.Failed);

        // a forced write is not silent
        var forced = await ImportAsync(key, JObject.Parse("""{"a":"text"}"""), Author, force: true);
        forced.Outcome.Should().Be(ViewImportOutcome.Created);
        forced.Version.Should().Be(1UL);
        forced.Warnings.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.SchemaViolation);
    }

    [Fact]
    public async Task Import_IsAuditedForTheActor()
    {
        await CreateAsync(Project, View, "rst.import-audit");
        var key = Key("rst.import-audit");
        var actor = new AuditActor { Kind = AuditActorKind.Account, Id = "user_importer", Name = "Importer" };

        await ImportAsync(key, JObject.Parse("""{"a":1}"""), actor);

        var history = await Annotations.GetAnnotationHistoryAsync(key);
        var entry = history!.Entries.First();
        entry.Action.Should().Be(AnnotationAuditAction.ConfigurationChanged);
        entry.Actor.Should().Be(actor);
        entry.ConfigurationVersion.Should().Be(1UL);
    }

    [Fact]
    public async Task Import_AfterAClearContinuesTheVersions()
    {
        const string project = "transfer-clear-versions";
        await CreateAsync(project, View, "unt.clear-parent.unit");
        var parent = Key("rst.clear-parent", project);
        var unit = Key("unt.clear-parent.unit", project);

        await Configurations.CreateOrUpdateConfigurationAsync(parent, JObject.Parse("""{"inherited":true}"""), Author);
        await Configurations.CreateOrUpdateConfigurationAsync(unit, JObject.Parse("""{"own":1}"""), Author);
        await Configurations.CreateOrUpdateConfigurationAsync(unit, JObject.Parse("""{"own":2}"""), Author);
        await Configurations.ClearConfigurationAsync(unit);

        // the read caches the inherited document in a new, empty item of version 0
        (await Configurations.GetRawConfigurationAsync(unit))!.Content["inherited"]!.Value<bool>().Should().BeTrue();

        var first = await ImportAsync(unit, JObject.Parse("""{"own":3}"""), Author);
        first.Should().Be(new ViewImportResult(ViewImportOutcome.Created, 3));

        var second = await ImportAsync(unit, JObject.Parse("""{"own":4}"""), Author);
        second.Should().Be(new ViewImportResult(ViewImportOutcome.Updated, 4));

        (await Configurations.GetConfigurationVersionsAsync(unit)).Select(version => version.Version).Should().Equal(1u, 2u, 3u, 4u);
    }

    [Fact]
    public async Task Store_ReadsAJobOnlyThroughItsViewAndGuardsWrites()
    {
        var store = Context.Services.GetRequiredService<IViewTransferStore>();
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var record = new ViewTransferRecord
        {
            Organization = TestConstants.Organization,
            Project = "transfer-store",
            View = View,
            Kind = ViewTransferKind.Export,
            Id = id,
            Export = new ViewExport
            {
                Id = id,
                Status = ViewTransferStatus.Queued,
                Format = ViewArchiveFormat.Git,
                FileName = "configurations.zip",
                CreatedAt = now,
                CreatedBy = AnnotationServiceTestExtensions.TestActor,
                ExpiresAt = now.AddHours(24),
            },
        };

        var created = await store.CreateAsync(record);
        created.ETag.Should().NotBeNullOrEmpty();

        var read = await store.GetAsync(TestConstants.Organization, "transfer-store", View, ViewTransferKind.Export, id);
        read!.Export.Should().BeEquivalentTo(record.Export);
        (await store.GetAsync(TestConstants.Organization, "transfer-store", "other", ViewTransferKind.Export, id)).Should().BeNull();
        (await store.GetAsync(TestConstants.Organization, "transfer-store", View, ViewTransferKind.Import, id)).Should().BeNull();

        (await store.CountActiveAsync(TestConstants.Organization, "transfer-store", now.AddMinutes(-5), now.AddMinutes(-5))).Should().Be(1);
        (await store.CountActiveAsync(TestConstants.Organization, "transfer-store", now.AddMinutes(5), now.AddMinutes(-5))).Should().Be(0);

        var running = await store.TryReplaceAsync(read with { Export = read.Export! with { Status = ViewTransferStatus.Running } });
        running.Should().NotBeNull();

        // a running job has its own window: one that has not saved progress since it is not counted
        (await store.CountActiveAsync(TestConstants.Organization, "transfer-store", now.AddMinutes(-5), now.AddMinutes(-5))).Should().Be(1);
        (await store.CountActiveAsync(TestConstants.Organization, "transfer-store", now.AddMinutes(-5), now.AddMinutes(5))).Should().Be(0);

        // the first read's ETag is stale now
        (await store.TryReplaceAsync(read with { Export = read.Export! with { Status = ViewTransferStatus.Failed } })).Should().BeNull();

        var succeeded = await store.TryReplaceAsync(running! with { Export = running.Export! with { Status = ViewTransferStatus.Succeeded } });
        succeeded.Should().NotBeNull();
        (await store.CountActiveAsync(TestConstants.Organization, "transfer-store", now.AddMinutes(-5), now.AddMinutes(-5))).Should().Be(0);
    }

    [Fact]
    public async Task Store_KeepsTheOptionsAndTheSummariesOfAnImport()
    {
        var store = Context.Services.GetRequiredService<IViewTransferStore>();
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var record = new ViewTransferRecord
        {
            Organization = TestConstants.Organization,
            Project = "transfer-store-import",
            View = View,
            Kind = ViewTransferKind.Import,
            Id = id,
            Import = new ViewImport
            {
                Id = id,
                Status = ViewTransferStatus.Succeeded,
                Include = [ViewArchiveContent.Schemas, ViewArchiveContent.Configurations],
                Existing = ViewImportExisting.Skip,
                IsDryRun = true,
                CreatedAt = now,
                CreatedBy = AnnotationServiceTestExtensions.TestActor,
                ExpiresAt = now.AddHours(24),
                Summary = new ViewImportSummary { Created = 1, Skipped = 1, Warnings = 1, ExcludedFiles = 2, IgnoredFiles = 3 },
                Summaries =
                [
                    new ViewImportSummary { Content = ViewArchiveContent.Schemas, Created = 1, Warnings = 1 },
                    new ViewImportSummary { Content = ViewArchiveContent.Configurations, Skipped = 1 },
                ],
                Items =
                [
                    new ViewImportItem
                    {
                        Path = "schemas/sbt.north.exe.schema.json",
                        Kind = ViewDocumentKind.DescendantTypeSchema,
                        AnnotationType = AnnotationTypeCodes.Execution,
                        AnnotationKey = "sbt.north",
                        Outcome = ViewImportOutcome.Created,
                        Warnings = [new ViewImportWarning { Code = ErrorCodes.SchemaViolation, Message = "forced" }],
                    },
                    new ViewImportItem
                    {
                        Path = "configurations/rst.pay.conf.json",
                        Kind = ViewDocumentKind.Configuration,
                        AnnotationKey = "rst.pay",
                        Outcome = ViewImportOutcome.Skipped,
                        ErrorCode = ErrorCodes.DocumentExists,
                        Message = "kept",
                    },
                ],
            },
        };

        await store.CreateAsync(record);
        var read = await store.GetAsync(TestConstants.Organization, "transfer-store-import", View, ViewTransferKind.Import, id);

        read!.Import.Should().BeEquivalentTo(record.Import);
    }

    [Fact]
    public async Task Transfer_RoundTripsEveryKindIntoAnotherView()
    {
        const string project = "transfer-round-trip";
        const string organization = TestConstants.Organization;
        await SeedViewAsync(project);
        var transfers = new Transfers(Context.Services);

        var export = await transfers.ExportAsync(project, View, ViewArchiveFormat.Folder);
        export.Job.Status.Should().Be(ViewTransferStatus.Succeeded);
        (export.Job.AnnotationCount, export.Job.SchemaCount, export.Job.TemplateCount, export.Job.ConfigurationCount).Should().Be((6, 3, 1, 2));

        // a dry run into a fresh view writes nothing and reports what the real run does
        var dryRun = await transfers.ImportAsync(project, "rt-copy", export.Archive, dryRun: true);
        dryRun.Status.Should().Be(ViewTransferStatus.Succeeded);
        (await Annotations.ExistAnnotationAsync(Key("rst.rt-pay", project, "rt-copy"))).Should().BeFalse();
        (await Templates.GetTemplateAsync(organization, project, "rt-copy", AnnotationTypeCodes.Execution)).Should().BeNull();

        var imported = await transfers.ImportAsync(project, "rt-copy", export.Archive);
        imported.Status.Should().Be(ViewTransferStatus.Succeeded);
        imported.Summaries.Should().Equal(
            new ViewImportSummary { Content = ViewArchiveContent.Annotations, Created = 6 },
            new ViewImportSummary { Content = ViewArchiveContent.Schemas, Created = 3 },
            new ViewImportSummary { Content = ViewArchiveContent.Templates, Created = 1 },
            new ViewImportSummary { Content = ViewArchiveContent.Configurations, Created = 2 });
        imported.Summary.Should().Be(new ViewImportSummary { Created = 12, IgnoredFiles = 3 });
        dryRun.Summaries.Should().Equal(imported.Summaries);
        dryRun.Items!.Select(item => (item.Path, item.Outcome)).Should().Equal(imported.Items!.Select(item => (item.Path, item.Outcome)));

        // the copy holds what the source holds
        var source = await Annotations.GetAnnotationAsync(Key("rst.rt-pay", project));
        var copy = await Annotations.GetAnnotationAsync(Key("rst.rt-pay", project, "rt-copy"));
        AnnotationAuditDiff.Compute(source!, copy! with { ViewName = View }, 1000).IsEmpty.Should().BeTrue();
        copy!.Values!["since"].ToString().Should().Be("2026-10-01T08:00:00Z");
        (await Annotations.GetAnnotationAsync(Key("unt.rt-pay.rt-unit", project, "rt-copy")))!.Title.Should().Be("Requests");

        var template = await Templates.GetTemplateAsync(organization, project, "rt-copy", AnnotationTypeCodes.Execution);
        JToken.DeepEquals(template!.Content, JObject.Parse("""{"timeout":30,"retries":3}""")).Should().BeTrue();
        var schema = await Schemas.GetDescendantTypeSchemaAsync(organization, project, "rt-copy", "sbt.rt-north", AnnotationTypeCodes.Execution);
        JToken.DeepEquals(schema!.Content, StringProperty("zone")).Should().BeTrue();
        var configuration = await Configurations.GetConfigurationVersionContentAsync(Key("rst.rt-pay", project, "rt-copy"), 1);
        JToken.DeepEquals(configuration!.Content, (await Configurations.GetConfigurationVersionContentAsync(Key("rst.rt-pay", project), 2))!.Content).Should().BeTrue();

        // the import is idempotent: a second run, as after a cut-off invocation, finds every document unchanged
        var again = await transfers.ImportAsync(project, "rt-copy", export.Archive);
        again.Summary.Should().Be(new ViewImportSummary { Unchanged = 12, IgnoredFiles = 3 });
        again.Items.Should().BeEmpty();

        // Skip keeps a document the copy changed, Overwrite replaces it
        await Configurations.CreateOrUpdateConfigurationAsync(Key("rst.rt-pay", project, "rt-copy"), JObject.Parse("""{"tier":"bronze"}"""), Author);
        var skipped = await transfers.ImportAsync(project, "rt-copy", export.Archive, existing: ViewImportExisting.Skip);
        skipped.Items!.Select(item => (item.Kind, item.AnnotationKey, item.Outcome, item.ErrorCode)).Should().Equal(
            (ViewDocumentKind.Configuration, "rst.rt-pay", ViewImportOutcome.Skipped, ErrorCodes.DocumentExists));
        var overwritten = await transfers.ImportAsync(project, "rt-copy", export.Archive, dryRun: true);
        overwritten.Items!.Select(item => (item.Kind, item.AnnotationKey, item.Outcome)).Should().Equal(
            (ViewDocumentKind.Configuration, "rst.rt-pay", ViewImportOutcome.Updated));
    }

    [Fact]
    public async Task Transfer_KeepsTheTextOfNumbersAndDatesOfValues()
    {
        const string project = "transfer-values-text";
        var transfers = new Transfers(Context.Services);
        var key = Key("rst.values-text", project);

        // a point read answers 17 digits for some doubles (3.1415926535897931), a query the fewest (3.141592653589793);
        // a date string parsed as a date, as the SDK's serializer of the LINQ queries does, loses its offset and its
        // milliseconds
        await Annotations.SetAnnotationAsync(
            TestConstants.Organization,
            AnnotationFactory.CreateDefault(key.Annotation, project, View) with
            {
                Values = new Dictionary<string, object>
                {
                    ["pi"] = Math.PI,
                    ["third"] = 1.0 / 3,
                    ["sum"] = 0.1 + 0.2,
                    ["huge"] = 1e300,
                    ["tiny"] = 1e-300,
                    ["offset"] = "2026-01-01T10:00:00+02:00",
                    ["millis"] = "2026-01-01T10:00:00.000Z",
                    ["nested"] = new { list = new[] { 2.0 / 3 }, at = "2026-01-01T10:00:00+02:00" },
                },
            },
            Author);
        var stored = await Annotations.GetAnnotationAsync(key);

        var export = await transfers.ExportAsync(project, View, ViewArchiveFormat.Folder);
        var file = ReadEntry(export.Archive, "annotations/rst.values-text.json");
        var imported = await transfers.ImportAsync(project, View, export.Archive, include: [ViewArchiveContent.Annotations]);
        var skipped = await transfers.ImportAsync(project, View, export.Archive, include: [ViewArchiveContent.Annotations], existing: ViewImportExisting.Skip);

        file.Should().Contain("\"offset\": \"2026-01-01T10:00:00+02:00\"").And.Contain("\"millis\": \"2026-01-01T10:00:00.000Z\"");
        file.Should().Contain("\"at\": \"2026-01-01T10:00:00+02:00\"");
        imported.Status.Should().Be(ViewTransferStatus.Succeeded);
        imported.Summaries.Should().Equal(new ViewImportSummary { Content = ViewArchiveContent.Annotations, Unchanged = 1 });
        imported.Items.Should().BeEmpty();
        skipped.Summaries.Should().Equal(imported.Summaries);
        (await Annotations.GetAnnotationHistoryAsync(key))!.Entries.Select(entry => entry.Action).Should().Equal(AnnotationAuditAction.Created);
        AnnotationAuditDiff.Compute(stored!, (await Annotations.GetAnnotationAsync(key))!, 1000).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task Transfer_ImportsTheWorkingTreeOfAGitExportWithAnIncludeFilter()
    {
        const string project = "transfer-git-trip";
        const string organization = TestConstants.Organization;
        await SeedViewAsync(project);
        var transfers = new Transfers(Context.Services);

        var export = await transfers.ExportAsync(project, View, ViewArchiveFormat.Git);
        var imported = await transfers.ImportAsync(
            project,
            "git-copy",
            export.Archive,
            include: [ViewArchiveContent.Configurations, ViewArchiveContent.Annotations]);

        // the .git folder is hidden, and the git format has no export.json and no version files
        imported.Status.Should().Be(ViewTransferStatus.Succeeded);
        imported.Include.Should().Equal(ViewArchiveContent.Annotations, ViewArchiveContent.Configurations);
        imported.Summaries.Should().Equal(
            new ViewImportSummary { Content = ViewArchiveContent.Annotations, Created = 6 },
            new ViewImportSummary { Content = ViewArchiveContent.Configurations, Created = 2 });
        imported.Summary.Should().Be(new ViewImportSummary { Created = 8, ExcludedFiles = 4 });

        (await Configurations.GetConfigurationVersionContentAsync(Key("exe.rt-north.rt-pay.rt-prod", project, "git-copy"), 1))!.Content["zone"]!.Value<string>().Should().Be("west");
        (await Templates.GetTemplateAsync(organization, project, "git-copy", AnnotationTypeCodes.Execution)).Should().BeNull();
        (await Schemas.ListSchemasAsync(organization, project, "git-copy")).Should().BeEmpty();
    }

    private static JObject StringProperty(string name)
    {
        return new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject { [name] = new JObject { ["type"] = "string" } },
        };
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

    private static string ReadEntry(byte[] archive, string path)
    {
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry(path)!.Open());
        return reader.ReadToEnd();
    }

    private static FullKey Key(string annotationKey, string project = Project, string view = View)
    {
        return FullKey.Create(AnnotationKey.Parse(annotationKey), TestConstants.Organization, project, view);
    }

    private Task CreateAsync(string project, string view, params string[] keys)
    {
        return Annotations.CreateAnnotationsFromStringAsync(TestConstants.Organization, project, view, keys, AnnotationServiceTestExtensions.TestActor);
    }

    private Task<ViewImportResult> ImportAsync(FullKey key, JObject content, AuditActor actor, bool force = false, bool dryRun = false)
    {
        return Configurations.ImportConfigurationAsync(
            key,
            content,
            new ViewImportOptions { Actor = actor, Force = force, DryRun = dryRun },
            new ViewImportOverlay());
    }

    // Every kind in the default view of the project: six annotations, two of them with content; a type, a
    // descendant-type and an annotation schema; a template with two versions; two configurations, one with two
    // versions. Dates are kept as strings, so they must survive the round trip.
    private async Task SeedViewAsync(string project)
    {
        const string organization = TestConstants.Organization;
        await CreateAsync(project, View, "unt.rt-pay.rt-unit", "exe.rt-north.rt-pay.rt-prod");
        await Annotations.SetAnnotationAsync(
            organization,
            AnnotationFactory.CreateDefault(AnnotationKey.Parse("rst.rt-pay"), project, View) with
            {
                Title = "Payments",
                Labels = ["eu"],
                Values = new Dictionary<string, object> { ["tier"] = "gold", ["since"] = "2026-10-01T08:00:00Z", ["limit"] = 1000 },
                TimeZone = "Europe/Prague",
            },
            Author);
        await Annotations.SetAnnotationAsync(
            organization,
            AnnotationFactory.CreateDefault(AnnotationKey.Parse("unt.rt-pay.rt-unit"), project, View) with { Title = "Requests" },
            Author);

        await Schemas.SetSchemaAsync(organization, project, View, AnnotationTypeCodes.Execution, StringProperty("region"), Author, false);
        await Schemas.SetDescendantTypeSchemaAsync(organization, project, View, "sbt.rt-north", AnnotationTypeCodes.Execution, StringProperty("zone"), Author, false);
        await Schemas.SetAnnotationSchemaAsync(organization, project, View, "rst.rt-pay", StringProperty("tier"), Author, false);

        await Templates.CreateOrUpdateTemplateAsync(organization, project, View, AnnotationTypeCodes.Execution, JObject.Parse("""{"timeout":30}"""), Author);
        await Templates.CreateOrUpdateTemplateAsync(organization, project, View, AnnotationTypeCodes.Execution, JObject.Parse("""{"retries":3}"""), Author);

        await Configurations.CreateOrUpdateConfigurationAsync(Key("rst.rt-pay", project), JObject.Parse("""{"tier":"silver"}"""), Author);
        await Configurations.CreateOrUpdateConfigurationAsync(Key("rst.rt-pay", project), JObject.Parse("""{"tier":"gold","since":"2026-10-01T08:00:00Z"}"""), Author);
        await Configurations.CreateOrUpdateConfigurationAsync(Key("exe.rt-north.rt-pay.rt-prod", project), JObject.Parse("""{"region":"eu","zone":"west"}"""), Author);
    }

    // The transfer service over the Cosmos DB services and job store, with the archives and the queue in memory. Each
    // call runs its job to the end.
    private sealed class Transfers
    {
        private readonly MemoryQueue _queue = new();
        private readonly ViewTransferService _service;

        public Transfers(IServiceProvider services)
        {
            _service = new ViewTransferService(
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

        public async Task<(ViewExport Job, byte[] Archive)> ExportAsync(string project, string view, ViewArchiveFormat format)
        {
            var started = await _service.StartExportAsync(TestConstants.Organization, project, view, format, null, Author);
            await _service.ProcessAsync(_queue.Messages[^1]);

            var (export, archive) = await _service.OpenExportArchiveAsync(TestConstants.Organization, project, view, started.Id);
            await using (archive)
            {
                using var buffer = new MemoryStream();
                await archive.CopyToAsync(buffer);
                return (export, buffer.ToArray());
            }
        }

        public async Task<ViewImport> ImportAsync(
            string project,
            string view,
            byte[] archive,
            IReadOnlyList<ViewArchiveContent>? include = null,
            ViewImportExisting existing = ViewImportExisting.Overwrite,
            bool dryRun = false)
        {
            using var stream = new MemoryStream(archive);
            var started = await _service.StartImportAsync(TestConstants.Organization, project, view, stream, "export.zip", include, existing, false, dryRun, Author);
            await _service.ProcessAsync(_queue.Messages[^1]);
            return (await _service.GetImportAsync(TestConstants.Organization, project, view, started.Id))!;
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
