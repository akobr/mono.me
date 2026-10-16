using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Transferring;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The imports of annotation, schema, template and configuration files: outcomes, warnings, Existing, Force, dry runs,
// the overlay and the audit. Every test works in its own project.
public class CosmosViewImportTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Organization = TestConstants.Organization;
    private const string View = Constants.DefaultViewName;
    private static readonly AuditActor Importer = AuditActor.Account("user_importer", "Importer");
    private static readonly AuditActor Creator = AnnotationServiceTestExtensions.TestActor;

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    private IConfigurationService Configurations => Context.Services.GetRequiredService<IConfigurationService>();

    private IConfigurationTemplateService Templates => Context.Services.GetRequiredService<IConfigurationTemplateService>();

    private IConfigurationSchemaService Schemas => Context.Services.GetRequiredService<IConfigurationSchemaService>();

    private Container Container => Context.Services
        .GetRequiredService<IContainerRepositoryProvider>()
        .GetOrganizationContainer(Organization)
        .Container;

    [Fact]
    public async Task ImportAnnotation_CreatesAMissingKey()
    {
        const string project = "import-create";
        var overlay = new ViewImportOverlay();

        var result = await Annotations.ImportAnnotationAsync(
            Key("rst.payments", project),
            Document("""{ "Title": "Payments", "Labels": ["eu"], "Values": { "tier": "gold" } }"""),
            Options(),
            overlay);

        result.Should().Be(new ViewImportResult(ViewImportOutcome.Created));

        var stored = await GetAsync("rst.payments", project);
        stored.Title.Should().Be("Payments");
        stored.Labels.Should().Equal("eu");
        stored.Values!["tier"].ToString().Should().Be("gold");
        stored.Created!.By.Should().Be(Importer);
        overlay.ActiveAnnotations.Should().BeEquivalentTo("rst.payments");
        overlay.RecreatedAnnotations.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportAnnotation_CreatesTheMissingAncestorsTheArchiveDoesNotHoldWithAWarning()
    {
        const string project = "import-ancestors";
        var overlay = new ViewImportOverlay();

        // the subject is in the archive, so it was imported earlier in the pass; the other ancestors are not
        (await Annotations.ImportAnnotationAsync(Key("sbt.northwind", project), Document("""{ "Title": "Northwind" }"""), Options(), overlay))
            .Outcome.Should().Be(ViewImportOutcome.Created);

        var result = await Annotations.ImportAnnotationAsync(
            Key("exe.northwind.payments.prod", project),
            Document("""{ "Title": "Production" }"""),
            Options(),
            overlay);

        result.Outcome.Should().Be(ViewImportOutcome.Created);
        result.Warnings.Should().Equal(new ViewImportWarning
        {
            Code = ErrorCodes.AncestorCreated,
            Message = "The missing ancestors rst.payments, usg.northwind.payments, cnt.northwind.prod are created with defaults.",
        });

        (await GetAsync("sbt.northwind", project)).Title.Should().Be("Northwind");
        (await GetAsync("cnt.northwind.prod", project)).Title.Should().BeNull();
        (await GetAsync("exe.northwind.payments.prod", project)).Title.Should().Be("Production");
        overlay.ActiveAnnotations.Should().BeEquivalentTo(
            "sbt.northwind",
            "rst.payments",
            "usg.northwind.payments",
            "cnt.northwind.prod",
            "exe.northwind.payments.prod");
        overlay.RecreatedAnnotations.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportAnnotation_LeavesAnEqualDocumentUnchanged()
    {
        const string project = "import-unchanged";
        var key = Key("unt.payments.ledger", project);
        await Annotations.CreateAnnotationAsync(
            Organization,
            (Unit)Model("unt.payments.ledger", project) with
            {
                Title = "Ledger",
                Description = "The ledger of payments",
                Labels = ["eu", "prod"],
                Values = new Dictionary<string, object> { ["limit"] = 10, ["since"] = "2026-01-01T00:00:00Z", ["nested"] = new { a = 1 } },
                ValidFrom = DateTimeOffset.Parse("2026-01-01T00:00:00+02:00"),
                TimeZone = "Europe/Prague",
                UnitType = "service",
            },
            Creator);

        // the file an export writes
        var file = Document(ViewAnnotationDocument.Write(await GetAsync("unt.payments.ledger", project)).ToString());

        (await Annotations.ImportAnnotationAsync(key, file, Options(), new ViewImportOverlay()))
            .Should().Be(new ViewImportResult(ViewImportOutcome.Unchanged));
        (await Annotations.ImportAnnotationAsync(key, file, Options(ViewImportExisting.Skip), new ViewImportOverlay()))
            .Should().Be(new ViewImportResult(ViewImportOutcome.Unchanged));

        var history = await Annotations.GetAnnotationHistoryAsync(key);
        history!.Entries.Select(entry => entry.Action).Should().Equal(AnnotationAuditAction.Created);
    }

    [Fact]
    public async Task ImportAnnotation_SkipKeepsADifferentDocumentAndOverwriteReplacesIt()
    {
        const string project = "import-overwrite";
        var key = Key("sbt.acme", project);
        await Annotations.CreateAnnotationAsync(Organization, Model("sbt.acme", project) with { Title = "Old", Description = "Kept by a merge", Labels = ["eu"] }, Creator);
        await CreateAsync(project, "cnt.acme.prod");
        var file = Document("""{ "Title": "New" }""");

        var skipped = await Annotations.ImportAnnotationAsync(key, file, Options(ViewImportExisting.Skip), new ViewImportOverlay());

        skipped.Should().Be(new ViewImportResult(
            ViewImportOutcome.Skipped,
            ErrorCode: ErrorCodes.DocumentExists,
            Message: "The view has a different annotation sbt.acme, and Skip keeps it."));
        (await GetAsync("sbt.acme", project)).Title.Should().Be("Old");

        var updated = await Annotations.ImportAnnotationAsync(key, file, Options(), new ViewImportOverlay());

        updated.Should().Be(new ViewImportResult(ViewImportOutcome.Updated));
        var subject = (Subject)await GetAsync("sbt.acme", project);
        subject.Title.Should().Be("New");
        subject.Description.Should().BeNull("an import replaces the content");
        subject.Labels.Should().BeNullOrEmpty();
        subject.ContextNames.Should().Equal("prod");
        subject.Created!.By.Should().Be(Creator);
        subject.Modified!.By.Should().Be(Importer);
    }

    [Fact]
    public async Task ImportAnnotation_RecreatesADeletedKeyWithOverwrite()
    {
        const string project = "import-recreate";
        var subject = Key("sbt.globex", project);
        await CreateAsync(project, "usg.globex.billing");
        await Configurations.CreateOrUpdateConfigurationAsync(subject, JObject.Parse("""{ "a": 1 }"""), Creator);
        await Configurations.CreateOrUpdateConfigurationAsync(subject, JObject.Parse("""{ "a": 2 }"""), Creator);
        await Annotations.DeleteAnnotationAsync(subject);
        var overlay = new ViewImportOverlay();

        var result = await Annotations.ImportAnnotationAsync(subject, Document("""{ "Title": "Globex again" }"""), Options(), overlay);

        result.Outcome.Should().Be(ViewImportOutcome.Created);
        result.Version.Should().BeNull();
        result.Warnings.Should().Equal(new ViewImportWarning
        {
            Code = ErrorCodes.AnnotationRecreated,
            Message = "The deleted annotation sbt.globex is re-created: its configuration goes to the versions and starts empty, and the children deleted with it stay deleted.",
        });

        var stored = (Subject)await GetAsync("sbt.globex", project);
        stored.Title.Should().Be("Globex again");
        stored.ResponsibilityNames.Should().BeEmpty();
        stored.Created!.By.Should().Be(Importer);

        // the configuration went to the versions, and the next version continues the numbering
        (await Configurations.HasConfigurationContentAsync(subject)).Should().BeFalse();
        (await Configurations.GetConfigurationVersionsAsync(subject)).Select(version => version.Version).Should().Equal(1u, 2u);

        // the child deleted with it stays deleted, as a root
        (await Annotations.GetDeletedAnnotationAsync(Key("usg.globex.billing", project)))!.Deletion!.IsRoot.Should().BeTrue();

        var history = await Annotations.GetAnnotationHistoryAsync(subject);
        history!.Entries.Select(entry => entry.Action).Should().Equal(
            AnnotationAuditAction.Recreated,
            AnnotationAuditAction.Deleted,
            AnnotationAuditAction.ConfigurationChanged,
            AnnotationAuditAction.ConfigurationChanged,
            AnnotationAuditAction.Created);
        history.Entries[0].Actor.Should().Be(Importer);
        history.Entries[0].Cause.Should().BeNull();
        history.Entries[0].DeletionId.Should().Be(history.Entries[1].DeletionId);

        overlay.ActiveAnnotations.Should().BeEquivalentTo("sbt.globex");
        overlay.RecreatedAnnotations.Should().BeEquivalentTo("sbt.globex");
    }

    [Fact]
    public async Task ImportAnnotation_RecreatesADeletedAncestorTheArchiveDoesNotHold()
    {
        const string project = "import-recreate-ancestor";
        await Annotations.CreateAnnotationAsync(Organization, Model("sbt.initech", project) with { Title = "Initech" }, Creator);
        await CreateAsync(project, "cnt.initech.prod");
        await Annotations.DeleteAnnotationAsync(Key("sbt.initech", project));
        var overlay = new ViewImportOverlay();

        var result = await Annotations.ImportAnnotationAsync(Key("usg.initech.billing", project), Document("""{ "Title": "Billing" }"""), Options(), overlay);

        result.Outcome.Should().Be(ViewImportOutcome.Created);
        result.Warnings.Should().Equal(
            new ViewImportWarning
            {
                Code = ErrorCodes.AncestorCreated,
                Message = "The missing ancestor rst.billing is created with defaults.",
            },
            new ViewImportWarning
            {
                Code = ErrorCodes.AnnotationRecreated,
                Message = "The deleted annotation sbt.initech is re-created: its configuration goes to the versions and starts empty, and the children deleted with it stay deleted.",
            });

        // re-created with defaults, like a missing ancestor
        var subject = (Subject)await GetAsync("sbt.initech", project);
        subject.Title.Should().BeNull();
        subject.ResponsibilityNames.Should().Equal("billing");
        subject.ContextNames.Should().BeEmpty();
        (await Annotations.GetDeletedAnnotationAsync(Key("cnt.initech.prod", project)))!.Deletion!.IsRoot.Should().BeTrue();

        var history = await Annotations.GetAnnotationHistoryAsync(Key("sbt.initech", project));
        history!.Entries[0].Action.Should().Be(AnnotationAuditAction.Recreated);
        history.Entries[0].Actor.Should().Be(Importer);
        history.Entries[0].Cause.Should().Be(new AnnotationAuditCause { Kind = AnnotationAuditCauseKind.Ancestor, Key = "usg.initech.billing" });

        overlay.ActiveAnnotations.Should().BeEquivalentTo("rst.billing", "sbt.initech", "usg.initech.billing");
        overlay.RecreatedAnnotations.Should().BeEquivalentTo("sbt.initech");
    }

    [Fact]
    public async Task ImportAnnotation_SkipLeavesDeletedAnnotationsDeleted()
    {
        const string project = "import-skip-deleted";
        await CreateAsync(project, "cnt.umbrella.lab");
        await Annotations.DeleteAnnotationAsync(Key("sbt.umbrella", project));
        var overlay = new ViewImportOverlay();
        var options = Options(ViewImportExisting.Skip);

        var own = await Annotations.ImportAnnotationAsync(Key("sbt.umbrella", project), Document("""{ "Title": "Umbrella" }"""), options, overlay);
        var child = await Annotations.ImportAnnotationAsync(Key("cnt.umbrella.lab", project), Document("{}"), options, overlay);
        var missing = await Annotations.ImportAnnotationAsync(Key("cnt.umbrella.office", project), Document("{}"), options, overlay);

        own.Should().Be(Skipped(ErrorCodes.AnnotationDeleted, "The annotation sbt.umbrella is deleted, and Skip leaves it deleted."));
        child.Should().Be(Skipped(ErrorCodes.AnnotationDeleted, "The annotations sbt.umbrella, cnt.umbrella.lab are deleted, and Skip leaves them deleted."));
        missing.Should().Be(Skipped(ErrorCodes.AnnotationDeleted, "The annotation sbt.umbrella is deleted, and Skip leaves it deleted."));

        (await Annotations.GetDeletedAnnotationAsync(Key("sbt.umbrella", project))).Should().NotBeNull();
        (await Annotations.GetDeletedAnnotationAsync(Key("cnt.umbrella.lab", project))).Should().NotBeNull();
        (await Annotations.ExistAnnotationAsync(Key("cnt.umbrella.office", project))).Should().BeFalse();
        overlay.ActiveAnnotations.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportAnnotation_DryRunWritesNothingAndRecordsTheKeysItWouldMakeActive()
    {
        const string project = "import-dry-run";
        await Annotations.CreateAnnotationAsync(Organization, Model("rst.hooli", project) with { Title = "Hooli" }, Creator);
        await CreateAsync(project, "sbt.pied");
        await Annotations.DeleteAnnotationAsync(Key("sbt.pied", project));
        var overlay = new ViewImportOverlay();
        var options = Options(dryRun: true);

        var updated = await Annotations.ImportAnnotationAsync(Key("rst.hooli", project), Document("""{ "Title": "Hooli XYZ" }"""), options, overlay);
        var recreated = await Annotations.ImportAnnotationAsync(Key("sbt.pied", project), Document("{}"), options, overlay);
        var created = await Annotations.ImportAnnotationAsync(Key("rst.raviga", project), Document("{}"), options, overlay);

        // the earlier files of the pass are in the overlay, so their descendant creates no ancestor
        var descendant = await Annotations.ImportAnnotationAsync(Key("usg.pied.raviga", project), Document("{}"), options, overlay);
        var orphan = await Annotations.ImportAnnotationAsync(Key("unt.endframe.box", project), Document("{}"), options, overlay);

        updated.Should().Be(new ViewImportResult(ViewImportOutcome.Updated));
        recreated.Outcome.Should().Be(ViewImportOutcome.Created);
        recreated.Warnings!.Select(warning => warning.Code).Should().Equal(ErrorCodes.AnnotationRecreated);
        created.Should().Be(new ViewImportResult(ViewImportOutcome.Created));
        descendant.Should().Be(new ViewImportResult(ViewImportOutcome.Created));
        orphan.Outcome.Should().Be(ViewImportOutcome.Created);
        orphan.Warnings!.Single().Message.Should().Be("The missing ancestor rst.endframe is created with defaults.");

        (await GetAsync("rst.hooli", project)).Title.Should().Be("Hooli");
        (await Annotations.GetAnnotationHistoryAsync(Key("rst.hooli", project)))!.Entries.Should().ContainSingle();
        (await Annotations.GetDeletedAnnotationAsync(Key("sbt.pied", project))).Should().NotBeNull();

        foreach (var key in new[] { "rst.raviga", "usg.pied.raviga", "rst.endframe", "unt.endframe.box" })
        {
            (await Annotations.ExistAnnotationAsync(Key(key, project))).Should().BeFalse($"'{key}' is not written");
        }

        overlay.ActiveAnnotations.Should().BeEquivalentTo("sbt.pied", "rst.raviga", "usg.pied.raviga", "rst.endframe", "unt.endframe.box");
        overlay.RecreatedAnnotations.Should().BeEquivalentTo("sbt.pied");
    }

    [Fact]
    public async Task ImportAnnotation_SkipsBelowAFailedAncestorAndFailsAnInvalidDocument()
    {
        const string project = "import-failed";
        var overlay = new ViewImportOverlay();
        overlay.AddFailedAnnotation(AnnotationKey.Parse("sbt.wayne"));

        var below = await Annotations.ImportAnnotationAsync(Key("cnt.wayne.manor", project), Document("{}"), Options(), overlay);
        var unknown = await Annotations.ImportAnnotationAsync(Key("rst.lex", project), Document("""{ "Owner": "Lex" }"""), Options(), overlay);
        var invalid = await Annotations.ImportAnnotationAsync(Key("rst.lex", project), Document($$"""{ "Title": "{{new string('x', 201)}}" }"""), Options(), overlay);

        below.Should().Be(Skipped(ErrorCodes.DependencyFailed, "The ancestor sbt.wayne failed in this import."));
        unknown.Should().Be(new ViewImportResult(ViewImportOutcome.Failed, ErrorCode: ErrorCodes.InvalidDocument, Message: "Unknown member 'Owner'."));
        invalid.Outcome.Should().Be(ViewImportOutcome.Failed);
        invalid.ErrorCode.Should().Be(ErrorCodes.InvalidAnnotation);

        foreach (var key in new[] { "sbt.wayne", "cnt.wayne.manor", "rst.lex" })
        {
            (await Annotations.ExistAnnotationAsync(Key(key, project))).Should().BeFalse($"'{key}' is not written");
        }

        overlay.ActiveAnnotations.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportAnnotation_WritesTheUsualAuditEntriesByTheImportingActor()
    {
        const string project = "import-audit";
        var key = Key("usg.audit.trail", project);
        var overlay = new ViewImportOverlay();

        await Annotations.ImportAnnotationAsync(key, Document("""{ "Title": "Trail" }"""), Options(), overlay);
        await Annotations.ImportAnnotationAsync(key, Document("""{ "Title": "Trail 2" }"""), Options(), overlay);

        // equal: no entry
        await Annotations.ImportAnnotationAsync(key, Document("""{ "Title": "Trail 2" }"""), Options(), overlay);

        var usage = await Annotations.GetAnnotationHistoryAsync(key);
        usage!.Entries.Select(entry => entry.Action).Should().Equal(AnnotationAuditAction.Updated, AnnotationAuditAction.Created);
        usage.Entries.Should().OnlyContain(entry => entry.Actor == Importer && entry.Cause == null);
        var title = usage.Entries[0].Fields!.Single(field => field.Field == nameof(Annotation.Title));
        title.Previous!.ToString().Should().Be("Trail");
        title.Value!.ToString().Should().Be("Trail 2");

        foreach (var ancestor in new[] { "rst.trail", "sbt.audit" })
        {
            var history = await Annotations.GetAnnotationHistoryAsync(Key(ancestor, project));
            var entry = history!.Entries.Should().ContainSingle().Subject;
            entry.Action.Should().Be(AnnotationAuditAction.Created);
            entry.Actor.Should().Be(Importer);
            entry.Cause.Should().Be(new AnnotationAuditCause { Kind = AnnotationAuditCauseKind.Ancestor, Key = "usg.audit.trail" });
        }
    }

    [Fact]
    public async Task ImportTemplate_CreatesThenReplacesThenLeavesAnEqualDocument()
    {
        const string project = "import-template";
        const string type = AnnotationTypeCodes.Execution;

        var created = await Templates.ImportTemplateAsync(Organization, project, View, type, JObject.Parse("""{ "a": 1, "list": [1, 2] }"""), Options());
        created.Should().Be(new ViewImportResult(ViewImportOutcome.Created, 1));

        // a merge would keep "a" and unite the lists; an import replaces the template
        var updated = await Templates.ImportTemplateAsync(Organization, project, View, "EXE", JObject.Parse("""{ "list": [3], "b": 2 }"""), Options());
        updated.Should().Be(new ViewImportResult(ViewImportOutcome.Updated, 2));

        var stored = await Templates.GetTemplateAsync(Organization, project, View, type);
        JToken.DeepEquals(stored!.Content, JObject.Parse("""{ "list": [3], "b": 2 }""")).Should().BeTrue();
        stored.Modified.By.Should().Be(Importer);

        var unchanged = await Templates.ImportTemplateAsync(Organization, project, View, type, JObject.Parse("""{ "b": 2, "list": [3] }"""), Options());
        unchanged.Should().Be(new ViewImportResult(ViewImportOutcome.Unchanged, 2));

        (await Templates.GetTemplateVersionsAsync(Organization, project, View, type)).Select(version => version.Version).Should().Equal(1u, 2u);
    }

    [Fact]
    public async Task ImportTemplate_AppliesTheDirectivesToTheDocumentAloneAndSkipsAnEmptyOne()
    {
        const string project = "import-template-directives";
        const string type = AnnotationTypeCodes.Context;
        await Templates.CreateOrUpdateTemplateAsync(Organization, project, View, type, JObject.Parse("""{ "a": 1, "kept": true }"""), Creator);

        var result = await Templates.ImportTemplateAsync(
            Organization,
            project,
            View,
            type,
            JObject.Parse("""{ "a": 2, "b": 2, "$remove": ["$.b"], "$patch": [{ "op": "add", "path": "/c", "value": 3 }] }"""),
            Options());

        result.Should().Be(new ViewImportResult(ViewImportOutcome.Updated, 2));
        JToken.DeepEquals((await Templates.GetTemplateAsync(Organization, project, View, type))!.Content, JObject.Parse("""{ "a": 2, "c": 3 }""")).Should().BeTrue();

        var broken = await Templates.ImportTemplateAsync(Organization, project, View, type, JObject.Parse("""{ "$patch": [{ "op": "remove", "path": "/missing" }] }"""), Options());
        broken.Outcome.Should().Be(ViewImportOutcome.Failed);
        broken.ErrorCode.Should().Be(ErrorCodes.PatchInvalid);

        var empty = await Templates.ImportTemplateAsync(Organization, project, View, type, new JObject(), Options());
        empty.Should().Be(Skipped(ErrorCodes.EmptyDocument, "An empty document is not imported; delete the template to clear it."));

        (await Templates.GetTemplateVersionsAsync(Organization, project, View, type)).Select(version => version.Version).Should().Equal(1u, 2u);
    }

    [Fact]
    public async Task ImportTemplate_SkipKeepsADifferentTemplate()
    {
        const string project = "import-template-skip";
        await Templates.CreateOrUpdateTemplateAsync(Organization, project, View, AnnotationTypeCodes.Execution, JObject.Parse("""{ "a": 1 }"""), Creator);
        var options = Options(ViewImportExisting.Skip);

        var skipped = await Templates.ImportTemplateAsync(Organization, project, View, AnnotationTypeCodes.Execution, JObject.Parse("""{ "a": 2 }"""), options);
        var unchanged = await Templates.ImportTemplateAsync(Organization, project, View, AnnotationTypeCodes.Execution, JObject.Parse("""{ "a": 1 }"""), options);
        var created = await Templates.ImportTemplateAsync(Organization, project, View, AnnotationTypeCodes.Unit, JObject.Parse("""{ "u": 1 }"""), options);

        skipped.Should().Be(Skipped(ErrorCodes.DocumentExists, "The view has a different template exe, and Skip keeps it."));
        unchanged.Should().Be(new ViewImportResult(ViewImportOutcome.Unchanged, 1));
        created.Should().Be(new ViewImportResult(ViewImportOutcome.Created, 1));

        var stored = await Templates.GetTemplateAsync(Organization, project, View, AnnotationTypeCodes.Execution);
        JToken.DeepEquals(stored!.Content, JObject.Parse("""{ "a": 1 }""")).Should().BeTrue();
        stored.Version.Should().Be(1UL);
    }

    [Fact]
    public async Task ImportTemplate_DryRunWritesNothing()
    {
        const string project = "import-template-dry-run";
        await Templates.CreateOrUpdateTemplateAsync(Organization, project, View, AnnotationTypeCodes.Execution, JObject.Parse("""{ "a": 1 }"""), Creator);
        var options = Options(dryRun: true);

        var created = await Templates.ImportTemplateAsync(Organization, project, View, AnnotationTypeCodes.Usage, JObject.Parse("""{ "g": 1 }"""), options);
        var updated = await Templates.ImportTemplateAsync(Organization, project, View, AnnotationTypeCodes.Execution, JObject.Parse("""{ "a": 2 }"""), options);

        created.Should().Be(new ViewImportResult(ViewImportOutcome.Created));
        updated.Should().Be(new ViewImportResult(ViewImportOutcome.Updated));
        (await Templates.GetTemplateAsync(Organization, project, View, AnnotationTypeCodes.Usage)).Should().BeNull();
        (await Templates.GetTemplateAsync(Organization, project, View, AnnotationTypeCodes.Execution))!.Content["a"]!.Value<int>().Should().Be(1);
        (await Templates.GetTemplateVersionsAsync(Organization, project, View, AnnotationTypeCodes.Execution)).Select(version => version.Version).Should().Equal(1u);
    }

    [Fact]
    public async Task ImportTemplate_ContinuesTheVersionsOfADeletedTemplate()
    {
        const string project = "import-template-versions";
        const string type = AnnotationTypeCodes.Unit;
        await Templates.CreateOrUpdateTemplateAsync(Organization, project, View, type, JObject.Parse("""{ "a": 1 }"""), Creator);
        await Templates.CreateOrUpdateTemplateAsync(Organization, project, View, type, JObject.Parse("""{ "a": 2 }"""), Creator);
        await Templates.DeleteTemplateAsync(Organization, project, View, type);

        var result = await Templates.ImportTemplateAsync(Organization, project, View, type, JObject.Parse("""{ "a": 3 }"""), Options());

        result.Should().Be(new ViewImportResult(ViewImportOutcome.Created, 3));
        (await Templates.GetTemplateVersionsAsync(Organization, project, View, type)).Select(version => version.Version).Should().Equal(1u, 2u, 3u);
    }

    [Fact]
    public async Task ImportTemplate_InvalidatesTheEffectiveDocuments()
    {
        const string project = "import-template-invalidate";
        await CreateAsync(project, "exe.customer.billing.prod");
        var execution = Key("exe.customer.billing.prod", project);
        await Configurations.CreateOrUpdateConfigurationAsync(execution, JObject.Parse("""{ "retries": 5 }"""), Creator);
        await Templates.CreateOrUpdateTemplateAsync(Organization, project, View, AnnotationTypeCodes.Execution, JObject.Parse("""{ "owner": "platform", "region": "eu" }"""), Creator);

        var first = await Configurations.GetRawConfigurationAsync(execution);
        (await HasCachedCalculationAsync(execution)).Should().BeTrue();

        var result = await Templates.ImportTemplateAsync(Organization, project, View, AnnotationTypeCodes.Execution, JObject.Parse("""{ "owner": "team" }"""), Options());

        result.Should().Be(new ViewImportResult(ViewImportOutcome.Updated, 2));
        (await HasCachedCalculationAsync(execution)).Should().BeFalse();

        var second = await Configurations.GetRawConfigurationAsync(execution);
        first!.Content["owner"]!.Value<string>().Should().Be("platform");
        second!.Content["owner"]!.Value<string>().Should().Be("team");
        second.Content.Should().NotContainKey("region");
        second.Content["retries"]!.Value<int>().Should().Be(5);
    }

    [Fact]
    public async Task ImportSchema_FailsAnInvalidSchemaEvenWithForceAndSkipsAnEmptyOne()
    {
        const string project = "import-schema-invalid";
        var target = TypeSchema(project, AnnotationTypeCodes.Execution);

        var invalid = await Schemas.ImportSchemaAsync(target, InvalidSchema(), Options(), new ViewImportOverlay());
        var forced = await Schemas.ImportSchemaAsync(target, InvalidSchema(), Options(force: true), new ViewImportOverlay());
        var empty = await Schemas.ImportSchemaAsync(target, new JObject(), Options(), new ViewImportOverlay());

        invalid.Outcome.Should().Be(ViewImportOutcome.Failed);
        invalid.ErrorCode.Should().Be(ErrorCodes.InvalidSchema);
        invalid.Message.Should().StartWith("Invalid JSON Schema: ");
        forced.Should().Be(invalid);
        empty.Should().Be(Skipped(ErrorCodes.EmptyDocument, "An empty document is not imported; delete the schema to clear it."));
        (await Schemas.GetSchemaAsync(Organization, project, View, AnnotationTypeCodes.Execution)).Should().BeNull();
    }

    [Fact]
    public async Task ImportSchema_LeavesAnEqualSchemaUnchangedAndSkipKeepsADifferentOne()
    {
        const string project = "import-schema-existing";
        await CreateAsync(project, "sbt.acme");
        await Schemas.SetSchemaAsync(Organization, project, View, AnnotationTypeCodes.Responsibility, Limit(), Creator, false);
        await Schemas.SetDescendantTypeSchemaAsync(Organization, project, View, "sbt.acme", AnnotationTypeCodes.Context, Limit(), Creator, false);
        var reordered = JObject.Parse("""{ "properties": { "limit": { "type": "number" } }, "type": "object" }""");
        var type = TypeSchema(project, AnnotationTypeCodes.Responsibility);
        var skip = Options(ViewImportExisting.Skip);

        (await Schemas.ImportSchemaAsync(type, reordered, Options(), new ViewImportOverlay())).Should().Be(new ViewImportResult(ViewImportOutcome.Unchanged, 1));
        (await Schemas.ImportSchemaAsync(type, reordered, skip, new ViewImportOverlay())).Should().Be(new ViewImportResult(ViewImportOutcome.Unchanged, 1));

        var skipped = await Schemas.ImportSchemaAsync(type, Name(), skip, new ViewImportOverlay());
        var descendant = await Schemas.ImportSchemaAsync(DescendantSchema(project, "sbt.acme", AnnotationTypeCodes.Context), Name(), skip, new ViewImportOverlay());
        var created = await Schemas.ImportSchemaAsync(AnnotationSchema(project, "sbt.acme"), Name(), skip, new ViewImportOverlay());

        skipped.Should().Be(Skipped(ErrorCodes.DocumentExists, "The view has a different type schema rst, and Skip keeps it."));
        descendant.Should().Be(Skipped(ErrorCodes.DocumentExists, "The view has a different cnt schema below sbt.acme, and Skip keeps it."));
        created.Should().Be(new ViewImportResult(ViewImportOutcome.Created, 1));

        var updated = await Schemas.ImportSchemaAsync(type, Name(), Options(), new ViewImportOverlay());

        updated.Should().Be(new ViewImportResult(ViewImportOutcome.Updated, 2));
        var stored = await Schemas.GetSchemaAsync(Organization, project, View, AnnotationTypeCodes.Responsibility);
        JToken.DeepEquals(stored!.Content, Name()).Should().BeTrue("an import replaces the schema");
        stored.Modified.By.Should().Be(Importer);
    }

    [Fact]
    public async Task ImportSchema_PassesAViolationThatTheArchiveFixes()
    {
        const string project = "import-schema-fixed";
        string[] keys = ["rst.fix-a", "rst.fix-b", "rst.fix-c"];
        await CreateAsync(project, keys);

        foreach (var key in keys)
        {
            await Configurations.CreateOrUpdateConfigurationAsync(Key(key, project), JObject.Parse("""{ "limit": "high" }"""), Creator);
        }

        await Annotations.DeleteAnnotationAsync(Key("rst.fix-c", project));
        var target = TypeSchema(project, AnnotationTypeCodes.Responsibility);

        // the stored documents violate it, the hidden one of the deleted annotation too, as on a save
        var alone = await Schemas.ImportSchemaAsync(target, Limit(), Options(), new ViewImportOverlay());

        alone.Should().Be(new ViewImportResult(
            ViewImportOutcome.Failed,
            ErrorCode: ErrorCodes.SchemaViolation,
            Message: "The configurations of rst.fix-a, rst.fix-b, rst.fix-c violate this schema."));
        (await Schemas.GetSchemaAsync(Organization, project, View, AnnotationTypeCodes.Responsibility)).Should().BeNull();

        // the archive replaces two of them, one through its directives, and re-creates the deleted annotation, which
        // empties its configuration
        var overlay = new ViewImportOverlay();
        overlay.SetConfiguration(AnnotationKey.Parse("rst.fix-a"), JObject.Parse("""{ "limit": 1 }"""));
        overlay.SetConfiguration(AnnotationKey.Parse("rst.fix-b"), JObject.Parse("""{ "limit": "high", "owner": "ops", "$remove": ["$.limit"] }"""));
        overlay.AddActiveAnnotation(AnnotationKey.Parse("rst.fix-c"), isRecreated: true);

        var result = await Schemas.ImportSchemaAsync(target, Limit(), Options(), overlay);

        result.Should().Be(new ViewImportResult(ViewImportOutcome.Created, 1));
        var stored = await Schemas.GetSchemaAsync(Organization, project, View, AnnotationTypeCodes.Responsibility);
        JToken.DeepEquals(stored!.Content, Limit()).Should().BeTrue();
        stored.Modified.By.Should().Be(Importer);
        overlay.Configurations["rst.fix-b"].Should().ContainKey("$remove", "the check works on a copy");
    }

    [Fact]
    public async Task ImportSchema_FailsAViolationTheArchiveLeavesAndForceWritesItWithAWarning()
    {
        const string project = "import-schema-forced";
        await CreateAsync(project, "rst.left", "rst.right");
        await Configurations.CreateOrUpdateConfigurationAsync(Key("rst.left", project), JObject.Parse("""{ "limit": "high" }"""), Creator);
        await Configurations.CreateOrUpdateConfigurationAsync(Key("rst.right", project), JObject.Parse("""{ "limit": "low" }"""), Creator);
        var overlay = new ViewImportOverlay();
        overlay.SetConfiguration(AnnotationKey.Parse("rst.right"), JObject.Parse("""{ "limit": 2 }"""));
        var target = TypeSchema(project, AnnotationTypeCodes.Responsibility);
        var warning = new ViewImportWarning
        {
            Code = ErrorCodes.SchemaViolation,
            Message = "Force stores this schema although the configuration of rst.left violates it.",
        };

        var failed = await Schemas.ImportSchemaAsync(target, Limit(), Options(), overlay);
        var checkedForce = await Schemas.ImportSchemaAsync(target, Limit(), Options(dryRun: true, force: true), overlay);

        failed.Should().Be(new ViewImportResult(
            ViewImportOutcome.Failed,
            ErrorCode: ErrorCodes.SchemaViolation,
            Message: "The configuration of rst.left violates this schema."));
        checkedForce.Outcome.Should().Be(ViewImportOutcome.Created);
        checkedForce.Version.Should().BeNull();
        checkedForce.Warnings.Should().Equal(warning);
        (await Schemas.GetSchemaAsync(Organization, project, View, AnnotationTypeCodes.Responsibility)).Should().BeNull();

        var forced = await Schemas.ImportSchemaAsync(target, Limit(), Options(force: true), overlay);

        forced.Outcome.Should().Be(ViewImportOutcome.Created);
        forced.Version.Should().Be(1UL);
        forced.Warnings.Should().Equal(warning);
        var stored = await Schemas.GetSchemaAsync(Organization, project, View, AnnotationTypeCodes.Responsibility);
        JToken.DeepEquals(stored!.Content, Limit()).Should().BeTrue();
    }

    [Fact]
    public async Task ImportSchema_ChecksTheConfigurationsOfNewAnnotationsInTheOverlay()
    {
        const string project = "import-schema-new";
        var overlay = new ViewImportOverlay();

        foreach (var key in new[] { "rst.fresh", "sbt.acme", "exe.acme.billing.prod", "exe.other.billing.prod" })
        {
            overlay.AddActiveAnnotation(AnnotationKey.Parse(key));
        }

        overlay.SetConfiguration(AnnotationKey.Parse("rst.fresh"), JObject.Parse("""{ "limit": "high" }"""));
        overlay.SetConfiguration(AnnotationKey.Parse("exe.acme.billing.prod"), JObject.Parse("""{ "limit": "high" }"""));
        overlay.SetConfiguration(AnnotationKey.Parse("exe.other.billing.prod"), JObject.Parse("""{ "limit": "high" }"""));

        // a dry run: none of the annotations is stored
        var annotation = await Schemas.ImportSchemaAsync(AnnotationSchema(project, "rst.fresh"), Limit(), Options(dryRun: true), overlay);
        var descendant = await Schemas.ImportSchemaAsync(DescendantSchema(project, "sbt.acme", AnnotationTypeCodes.Execution), Limit(), Options(dryRun: true), overlay);

        annotation.Should().Be(new ViewImportResult(
            ViewImportOutcome.Failed,
            ErrorCode: ErrorCodes.SchemaViolation,
            Message: "The configuration of rst.fresh violates this schema."));
        descendant.Should().Be(new ViewImportResult(
            ViewImportOutcome.Failed,
            ErrorCode: ErrorCodes.SchemaViolation,
            Message: "The configuration of exe.acme.billing.prod violates this schema."));
    }

    [Fact]
    public async Task ImportSchema_ListsAtMostTwentyNonCompliantKeys()
    {
        const string project = "import-schema-many";
        var overlay = new ViewImportOverlay();
        var keys = Enumerable.Range(1, 25).Select(index => $"rst.k{index:D2}").ToList();

        foreach (var key in keys)
        {
            overlay.AddActiveAnnotation(AnnotationKey.Parse(key));
            overlay.SetConfiguration(AnnotationKey.Parse(key), JObject.Parse("""{ "limit": "high" }"""));
        }

        var result = await Schemas.ImportSchemaAsync(TypeSchema(project, AnnotationTypeCodes.Responsibility), Limit(), Options(dryRun: true), overlay);

        result.Outcome.Should().Be(ViewImportOutcome.Failed);
        result.Message.Should().Be($"The configurations of {string.Join(", ", keys.Take(20))} … and 5 more violate this schema.");
    }

    [Fact]
    public async Task ImportSchema_TakesTheOtherLayersFromTheArchive()
    {
        const string project = "import-schema-layers";
        await CreateAsync(project, "rst.layer");
        await Configurations.CreateOrUpdateConfigurationAsync(Key("rst.layer", project), JObject.Parse("""{ "limit": 5 }"""), Creator);
        await Schemas.SetAnnotationSchemaAsync(Organization, project, View, "rst.layer", Maximum(3), Creator, force: true);
        var target = TypeSchema(project, AnnotationTypeCodes.Responsibility);

        // the stored schema of the annotation is violated already
        (await Schemas.ImportSchemaAsync(target, Limit(), Options(), new ViewImportOverlay())).ErrorCode.Should().Be(ErrorCodes.SchemaViolation);

        // an invalid schema of the archive is not written, so the stored one stays
        var invalid = new ViewImportOverlay();
        invalid.SetSchema(SchemaKey.ForAnnotation("rst.layer"), InvalidSchema());
        (await Schemas.ImportSchemaAsync(target, Limit(), Options(), invalid)).ErrorCode.Should().Be(ErrorCodes.SchemaViolation);

        // the archive relaxes it
        var overlay = new ViewImportOverlay();
        overlay.SetSchema(SchemaKey.ForAnnotation("rst.layer"), Maximum(10));

        (await Schemas.ImportSchemaAsync(target, Limit(), Options(), overlay)).Should().Be(new ViewImportResult(ViewImportOutcome.Created, 1));
    }

    [Fact]
    public async Task ImportSchema_DryRunWritesNothing()
    {
        const string project = "import-schema-dry-run";
        await CreateAsync(project, "rst.dry");
        await Schemas.SetSchemaAsync(Organization, project, View, AnnotationTypeCodes.Execution, Limit(), Creator, false);
        var options = Options(dryRun: true);

        var updated = await Schemas.ImportSchemaAsync(TypeSchema(project, AnnotationTypeCodes.Execution), Name(), options, new ViewImportOverlay());
        var created = await Schemas.ImportSchemaAsync(AnnotationSchema(project, "rst.dry"), Limit(), options, new ViewImportOverlay());

        updated.Should().Be(new ViewImportResult(ViewImportOutcome.Updated));
        created.Should().Be(new ViewImportResult(ViewImportOutcome.Created));
        (await Schemas.GetSchemaVersionsAsync(Organization, project, View, AnnotationTypeCodes.Execution)).Select(version => version.Version).Should().Equal(1u);
        JToken.DeepEquals((await Schemas.GetSchemaAsync(Organization, project, View, AnnotationTypeCodes.Execution))!.Content, Limit()).Should().BeTrue();
        (await Schemas.GetAnnotationSchemaAsync(Organization, project, View, "rst.dry")).Should().BeNull();
    }

    [Fact]
    public async Task ImportSchema_SkipsTheSchemasOfAnnotationsThatAreNotActive()
    {
        const string project = "import-schema-annotations";
        await CreateAsync(project, "rst.gone");
        await Annotations.DeleteAnnotationAsync(Key("rst.gone", project));
        var overlay = new ViewImportOverlay();
        overlay.AddFailedAnnotation(AnnotationKey.Parse("rst.failed"));
        overlay.AddActiveAnnotation(AnnotationKey.Parse("sbt.coming"));

        var missing = await Schemas.ImportSchemaAsync(AnnotationSchema(project, "rst.ghost"), Limit(), Options(), overlay);
        var below = await Schemas.ImportSchemaAsync(DescendantSchema(project, "sbt.ghost", AnnotationTypeCodes.Context), Limit(), Options(), overlay);
        var deleted = await Schemas.ImportSchemaAsync(AnnotationSchema(project, "rst.gone"), Limit(), Options(), overlay);
        var failed = await Schemas.ImportSchemaAsync(AnnotationSchema(project, "rst.failed"), Limit(), Options(), overlay);
        var invalidKey = await Schemas.ImportSchemaAsync(DescendantSchema(project, "rst.gone", AnnotationTypeCodes.Subject), Limit(), Options(), overlay);

        // the overlay's annotation counts as active, which matters in a dry run
        var coming = await Schemas.ImportSchemaAsync(DescendantSchema(project, "sbt.coming", AnnotationTypeCodes.Context), Limit(), Options(dryRun: true), overlay);

        // a type schema has no annotation
        var type = await Schemas.ImportSchemaAsync(TypeSchema(project, AnnotationTypeCodes.Context), Limit(), Options(), overlay);

        missing.Should().Be(Skipped(ErrorCodes.AnnotationNotFound, $"The view {View} has no annotation rst.ghost."));
        below.Should().Be(Skipped(ErrorCodes.AnnotationNotFound, $"The view {View} has no annotation sbt.ghost."));
        deleted.Should().Be(Skipped(ErrorCodes.AnnotationDeleted, "The annotation rst.gone is deleted; restore it before importing its schemas."));
        failed.Should().Be(Skipped(ErrorCodes.DependencyFailed, "The annotation rst.failed failed in this import."));
        invalidKey.Should().Be(new ViewImportResult(
            ViewImportOutcome.Failed,
            ErrorCode: ErrorCodes.InvalidKey,
            Message: "The type sbt cannot descend from rst.gone, so it has no descendant-type schema there."));
        coming.Should().Be(new ViewImportResult(ViewImportOutcome.Created));
        type.Should().Be(new ViewImportResult(ViewImportOutcome.Created, 1));

        (await Schemas.ListSchemasAsync(Organization, project, View)).Select(schema => schema.AnnotationType).Should().Equal(AnnotationTypeCodes.Context);
    }

    [Fact]
    public async Task ImportConfiguration_SkipKeepsADifferentDocument()
    {
        const string project = "import-config-skip";
        await CreateAsync(project, "rst.kept", "rst.blank");
        await Configurations.CreateOrUpdateConfigurationAsync(Key("rst.kept", project), JObject.Parse("""{ "a": 1 }"""), Creator);
        var options = Options(ViewImportExisting.Skip);

        var skipped = await Configurations.ImportConfigurationAsync(Key("rst.kept", project), JObject.Parse("""{ "a": 2 }"""), options, new ViewImportOverlay());
        var unchanged = await Configurations.ImportConfigurationAsync(Key("rst.kept", project), JObject.Parse("""{ "a": 1 }"""), options, new ViewImportOverlay());
        var created = await Configurations.ImportConfigurationAsync(Key("rst.blank", project), JObject.Parse("""{ "b": 1 }"""), options, new ViewImportOverlay());

        skipped.Should().Be(Skipped(ErrorCodes.DocumentExists, "The view has a different configuration of rst.kept, and Skip keeps it."));
        unchanged.Should().Be(new ViewImportResult(ViewImportOutcome.Unchanged, 1));
        created.Should().Be(new ViewImportResult(ViewImportOutcome.Created, 1));
        (await Configurations.GetConfigurationVersionsAsync(Key("rst.kept", project))).Select(version => version.Version).Should().Equal(1u);
    }

    [Fact]
    public async Task ImportConfiguration_CountsTheAnnotationsOfTheOverlayInADryRun()
    {
        const string project = "import-config-overlay";
        await CreateAsync(project, "rst.recreated", "rst.stored");
        await Configurations.CreateOrUpdateConfigurationAsync(Key("rst.recreated", project), JObject.Parse("""{ "a": 1 }"""), Creator);
        await Annotations.DeleteAnnotationAsync(Key("rst.recreated", project));
        var overlay = new ViewImportOverlay();
        overlay.AddActiveAnnotation(AnnotationKey.Parse("rst.new"));
        overlay.AddActiveAnnotation(AnnotationKey.Parse("rst.recreated"), isRecreated: true);
        overlay.AddFailedAnnotation(AnnotationKey.Parse("rst.failed"));
        overlay.SetSchema(SchemaKey.ForType(AnnotationTypeCodes.Responsibility), JObject.Parse("""{ "type": "object", "properties": { "a": { "type": "number" } } }"""));
        var dryRun = Options(dryRun: true);

        var created = await Configurations.ImportConfigurationAsync(Key("rst.new", project), JObject.Parse("""{ "a": 1 }"""), dryRun, overlay);

        // equal to the stored document, but the re-create empties it
        var recreated = await Configurations.ImportConfigurationAsync(Key("rst.recreated", project), JObject.Parse("""{ "a": 1 }"""), dryRun, overlay);
        var failed = await Configurations.ImportConfigurationAsync(Key("rst.failed", project), JObject.Parse("""{ "a": 1 }"""), dryRun, overlay);

        // a dry run validates against the schemas of the archive, a real run against the stored ones, which it wrote
        var violating = await Configurations.ImportConfigurationAsync(Key("rst.stored", project), JObject.Parse("""{ "a": "x" }"""), dryRun, overlay);
        var written = await Configurations.ImportConfigurationAsync(Key("rst.stored", project), JObject.Parse("""{ "a": "x" }"""), Options(), overlay);

        created.Should().Be(new ViewImportResult(ViewImportOutcome.Created));
        recreated.Should().Be(new ViewImportResult(ViewImportOutcome.Created));
        failed.Should().Be(Skipped(ErrorCodes.DependencyFailed, "The annotation rst.failed failed in this import."));
        violating.Outcome.Should().Be(ViewImportOutcome.Failed);
        violating.ErrorCode.Should().Be(ErrorCodes.SchemaViolation);
        written.Should().Be(new ViewImportResult(ViewImportOutcome.Created, 1));

        (await Annotations.ExistAnnotationAsync(Key("rst.new", project))).Should().BeFalse();
        (await Annotations.GetDeletedAnnotationAsync(Key("rst.recreated", project))).Should().NotBeNull();
        (await Configurations.HasConfigurationContentAsync(Key("rst.new", project))).Should().BeFalse();
    }

    [Fact]
    public async Task ImportConfiguration_ForcedWriteCarriesAWarning()
    {
        const string project = "import-config-force";
        await CreateAsync(project, "rst.forced");
        await Schemas.SetSchemaAsync(Organization, project, View, AnnotationTypeCodes.Responsibility, Limit(), Creator, false);
        var key = Key("rst.forced", project);

        var rejected = await Configurations.ImportConfigurationAsync(key, JObject.Parse("""{ "limit": "high" }"""), Options(), new ViewImportOverlay());
        var checkedForce = await Configurations.ImportConfigurationAsync(key, JObject.Parse("""{ "limit": "high" }"""), Options(dryRun: true, force: true), new ViewImportOverlay());

        rejected.Outcome.Should().Be(ViewImportOutcome.Failed);
        rejected.ErrorCode.Should().Be(ErrorCodes.SchemaViolation);
        checkedForce.Outcome.Should().Be(ViewImportOutcome.Created);
        checkedForce.Version.Should().BeNull();
        (await Configurations.HasConfigurationContentAsync(key)).Should().BeFalse();

        var forced = await Configurations.ImportConfigurationAsync(key, JObject.Parse("""{ "limit": "high" }"""), Options(force: true), new ViewImportOverlay());

        forced.Outcome.Should().Be(ViewImportOutcome.Created);
        forced.Version.Should().Be(1UL);
        var warning = forced.Warnings.Should().ContainSingle().Subject;
        warning.Code.Should().Be(ErrorCodes.SchemaViolation);
        warning.Message.Should().StartWith("Force stores the document although it violates its schema: ");
        checkedForce.Warnings.Should().Equal(warning);
        (await Configurations.GetConfigurationVersionContentAsync(key, 1))!.Content["limit"]!.Value<string>().Should().Be("high");
    }

    private static ViewImportOptions Options(ViewImportExisting existing = ViewImportExisting.Overwrite, bool dryRun = false, bool force = false)
    {
        return new ViewImportOptions { Actor = Importer, Existing = existing, DryRun = dryRun, Force = force };
    }

    private static ViewSchemaTarget TypeSchema(string project, string annotationType)
    {
        return new ViewSchemaTarget(Organization, project, View, ViewDocumentKind.TypeSchema, annotationType, null);
    }

    private static ViewSchemaTarget AnnotationSchema(string project, string annotationKey)
    {
        return new ViewSchemaTarget(Organization, project, View, ViewDocumentKind.AnnotationSchema, null, annotationKey);
    }

    private static ViewSchemaTarget DescendantSchema(string project, string annotationKey, string annotationType)
    {
        return new ViewSchemaTarget(Organization, project, View, ViewDocumentKind.DescendantTypeSchema, annotationType, annotationKey);
    }

    // "limit" is a number
    private static JObject Limit()
    {
        return JObject.Parse("""{ "type": "object", "properties": { "limit": { "type": "number" } } }""");
    }

    private static JObject Name()
    {
        return JObject.Parse("""{ "type": "object", "properties": { "name": { "type": "string" } } }""");
    }

    private static JObject Maximum(int maximum)
    {
        return new JObject { ["properties"] = new JObject { ["limit"] = new JObject { ["maximum"] = maximum } } };
    }

    // NJsonSchema cannot resolve the reference
    private static JObject InvalidSchema()
    {
        return JObject.Parse("""{ "$ref": "#/definitions/missing" }""");
    }

    private static ViewImportResult Skipped(string errorCode, string message)
    {
        return new ViewImportResult(ViewImportOutcome.Skipped, ErrorCode: errorCode, Message: message);
    }

    // A file as the reader parses it.
    private static JObject Document(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return ViewAnnotationDocument.Parse(stream);
    }

    private static FullKey Key(string annotationKey, string project)
    {
        return FullKey.Create(AnnotationKey.Parse(annotationKey), Organization, project, View);
    }

    private static Annotation Model(string annotationKey, string project)
    {
        return AnnotationFactory.CreateDefault(AnnotationKey.Parse(annotationKey), project, View);
    }

    private Task CreateAsync(string project, params string[] keys)
    {
        return Annotations.CreateAnnotationsFromStringAsync(Organization, project, View, keys, Creator);
    }

    private async Task<Annotation> GetAsync(string annotationKey, string project)
    {
        var annotation = await Annotations.GetAnnotationAsync(Key(annotationKey, project));
        annotation.Should().NotBeNull($"'{annotationKey}' should be active");
        return annotation!;
    }

    private async Task<bool> HasCachedCalculationAsync(FullKey key)
    {
        var item = await Container.ReadItemAsync<JObject>($"{key.ViewName}.cnf.{key.Annotation}", new PartitionKey(key.GetPartitionKey()));
        return item.Resource["CalculatedContent"]?.Type == JTokenType.Object;
    }
}
