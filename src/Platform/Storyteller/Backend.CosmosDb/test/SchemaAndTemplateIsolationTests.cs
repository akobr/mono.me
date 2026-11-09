#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Entities;
using _42.Platform.Storyteller.Transferring;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Tenant isolation of configuration schemas and generate templates in the shared container "organizations". Each test
// has two organizations with the same project, views, annotations, schemas and templates, and different values ("a-…"
// in A, "b-…" in B), so a document of the other organization shows in any read. Reads through A see only A's documents;
// writes and deletes through A leave every item of B as it was, system properties included; and the organization-wide
// queries (the configurations a schema save checks, the cached calculations a template write invalidates) stay in A.
public class SchemaAndTemplateIsolationTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Project = "billing";
    private const string View = Constants.DefaultViewName;
    private const string OtherView = "prod";
    private const string SubjectName = "customer";
    private const string ResponsibilityName = "payments";
    private const string ContextName = "live";
    private const string RefundsName = "refunds";
    private const string PaymentsKey = "rst.payments";
    private const string RefundsKey = "rst.refunds";

    private static readonly AuditActor Actor = AuditActor.Account("isolation-test");

    private IConfigurationSchemaService Schemas => Context.Services.GetRequiredService<IConfigurationSchemaService>();

    private IConfigurationTemplateService Templates => Context.Services.GetRequiredService<IConfigurationTemplateService>();

    private IConfigurationService Configs => Context.Services.GetRequiredService<IConfigurationService>();

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    [Fact]
    public async Task SchemaReads_SeeOnlyTheSchemasOfTheirOrganization()
    {
        var (a, b) = Organizations("schema-reads");
        var executionA = await CreateExecutionAsync(a);
        var executionB = await CreateExecutionAsync(b);

        // the same identities in both, with their own histories: A has two versions of the type schema, B two of the
        // annotation schema and a subject type schema that A doesn't have; the type schemas type "name" differently
        await Schemas.SetSchemaAsync(a, Project, View, AnnotationTypeCodes.Responsibility, Schema("a-type-v1", "string"), Actor, false);
        await Schemas.SetSchemaAsync(a, Project, View, AnnotationTypeCodes.Responsibility, Schema("a-type-v2", "string"), Actor, false);
        await Schemas.SetAnnotationSchemaAsync(a, Project, View, PaymentsKey, Schema("a-annotation-v1"), Actor, false);
        await Schemas.SetDescendantTypeSchemaAsync(a, Project, View, PaymentsKey, AnnotationTypeCodes.Execution, Schema("a-descendant-v1"), Actor, false);
        await Schemas.SetSchemaAsync(b, Project, View, AnnotationTypeCodes.Responsibility, Schema("b-type-v1", "integer"), Actor, false);
        await Schemas.SetSchemaAsync(b, Project, View, AnnotationTypeCodes.Subject, Schema("b-subject-v1"), Actor, false);
        await Schemas.SetAnnotationSchemaAsync(b, Project, View, PaymentsKey, Schema("b-annotation-v1"), Actor, false);
        await Schemas.SetAnnotationSchemaAsync(b, Project, View, PaymentsKey, Schema("b-annotation-v2"), Actor, false);
        await Schemas.SetDescendantTypeSchemaAsync(b, Project, View, PaymentsKey, AnnotationTypeCodes.Execution, Schema("b-descendant-v1"), Actor, false);

        // the current documents
        var typeA = await Schemas.GetSchemaAsync(a, Project, View, AnnotationTypeCodes.Responsibility);
        DescriptionOf(typeA).Should().Be("a-type-v2");
        typeA!.Version.Should().Be(2);
        DescriptionOf(await Schemas.GetSchemaAsync(b, Project, View, AnnotationTypeCodes.Responsibility)).Should().Be("b-type-v1");
        (await Schemas.GetSchemaAsync(a, Project, View, AnnotationTypeCodes.Subject)).Should().BeNull();
        DescriptionOf(await Schemas.GetAnnotationSchemaAsync(a, Project, View, PaymentsKey)).Should().Be("a-annotation-v1");
        DescriptionOf(await Schemas.GetAnnotationSchemaAsync(b, Project, View, PaymentsKey)).Should().Be("b-annotation-v2");
        DescriptionOf(await Schemas.GetDescendantTypeSchemaAsync(a, Project, View, PaymentsKey, AnnotationTypeCodes.Execution)).Should().Be("a-descendant-v1");
        DescriptionOf(await Schemas.GetDescendantTypeSchemaAsync(b, Project, View, PaymentsKey, AnnotationTypeCodes.Execution)).Should().Be("b-descendant-v1");

        // the listings of the schema partition
        (await Schemas.ListSchemasAsync(a, Project, View))
            .Select(summary => (summary.Kind, summary.AnnotationType, summary.AnnotationKey, summary.Version))
            .Should().Equal(
                (ConfigurationSchemaKind.Type, AnnotationTypeCodes.Responsibility, null, 2UL),
                (ConfigurationSchemaKind.Annotation, null, PaymentsKey, 1UL),
                (ConfigurationSchemaKind.DescendantType, AnnotationTypeCodes.Execution, PaymentsKey, 1UL));
        (await Schemas.ListSchemasAsync(b, Project, View))
            .Select(summary => (summary.Kind, summary.AnnotationType, summary.AnnotationKey, summary.Version))
            .Should().Equal(
                (ConfigurationSchemaKind.Type, AnnotationTypeCodes.Responsibility, null, 1UL),
                (ConfigurationSchemaKind.Type, AnnotationTypeCodes.Subject, null, 1UL),
                (ConfigurationSchemaKind.Annotation, null, PaymentsKey, 2UL),
                (ConfigurationSchemaKind.DescendantType, AnnotationTypeCodes.Execution, PaymentsKey, 1UL));
        (await Schemas.GetSchemaKeysAsync(a, Project, View, TestContext.Current.CancellationToken)).Should().BeEquivalentTo(new[]
        {
            SchemaKey.ForType(AnnotationTypeCodes.Responsibility),
            SchemaKey.ForAnnotation(PaymentsKey),
            SchemaKey.ForDescendantType(PaymentsKey, AnnotationTypeCodes.Execution),
        });

        // the versions, and a version that only the other organization has
        (await Schemas.GetSchemaVersionsAsync(a, Project, View, AnnotationTypeCodes.Responsibility)).Select(version => version.Version).Should().Equal(1u, 2u);
        (await Schemas.GetSchemaVersionsAsync(b, Project, View, AnnotationTypeCodes.Responsibility)).Select(version => version.Version).Should().Equal(1u);
        (await Schemas.GetSchemaVersionsAsync(a, Project, View, AnnotationTypeCodes.Subject)).Should().BeEmpty();
        (await Schemas.GetAnnotationSchemaVersionsAsync(a, Project, View, PaymentsKey)).Select(version => version.Version).Should().Equal(1u);
        (await Schemas.GetAnnotationSchemaVersionsAsync(b, Project, View, PaymentsKey)).Select(version => version.Version).Should().Equal(1u, 2u);
        (await Schemas.GetDescendantTypeSchemaVersionsAsync(a, Project, View, PaymentsKey, AnnotationTypeCodes.Execution)).Should().ContainSingle();
        DescriptionOf(await Schemas.GetSchemaVersionContentAsync(a, Project, View, AnnotationTypeCodes.Responsibility, 1)).Should().Be("a-type-v1");
        (await Schemas.GetSchemaVersionContentAsync(b, Project, View, AnnotationTypeCodes.Responsibility, 2)).Should().BeNull();
        DescriptionOf(await Schemas.GetAnnotationSchemaVersionContentAsync(b, Project, View, PaymentsKey, 1)).Should().Be("b-annotation-v1");
        (await Schemas.GetAnnotationSchemaVersionContentAsync(a, Project, View, PaymentsKey, 2)).Should().BeNull();
        DescriptionOf(await Schemas.GetDescendantTypeSchemaVersionContentAsync(b, Project, View, PaymentsKey, AnnotationTypeCodes.Execution, 1)).Should().Be("b-descendant-v1");

        (await Schemas.GetSchemaVersionChangesAsync(a, Project, View, AnnotationTypeCodes.Responsibility, 2)).Stats.Additions.Should().BeGreaterThan(0);
        var typeChangesB = () => Schemas.GetSchemaVersionChangesAsync(b, Project, View, AnnotationTypeCodes.Responsibility, 2);
        await typeChangesB.Should().ThrowAsync<InvalidOperationException>();
        var annotationChangesA = () => Schemas.GetAnnotationSchemaVersionChangesAsync(a, Project, View, PaymentsKey, 2);
        await annotationChangesA.Should().ThrowAsync<InvalidOperationException>();

        // the histories of an export, oldest first, the type schemas before the descendant-type and annotation schemas
        (await Schemas.GetSchemaHistoriesAsync(a, Project, View, TestContext.Current.CancellationToken))
            .SelectMany(history => history.Versions)
            .Select(document => document.Content["description"]!.Value<string>())
            .Should().Equal("a-type-v1", "a-type-v2", "a-descendant-v1", "a-annotation-v1");
        (await Schemas.GetSchemaHistoriesAsync(b, Project, View, TestContext.Current.CancellationToken))
            .SelectMany(history => history.Versions)
            .Select(document => document.Content["description"]!.Value<string>())
            .Should().Equal("b-type-v1", "b-subject-v1", "b-descendant-v1", "b-annotation-v1", "b-annotation-v2");

        // the combined schemas and the validation take their layers from their own organization
        (await Schemas.GetCombinedSchemaAsync(a, Project, View, PaymentsKey))!.AppliedSchemas.Select(DescriptionOf)
            .Should().Equal("a-type-v2", "a-annotation-v1");
        (await Schemas.GetCombinedSchemaAsync(b, Project, View, PaymentsKey))!.AppliedSchemas.Select(DescriptionOf)
            .Should().Equal("b-type-v1", "b-annotation-v2");
        (await Schemas.GetCombinedSchemaAsync(a, Project, View, executionA.Annotation.ToString()))!.AppliedSchemas.Select(DescriptionOf)
            .Should().Equal("a-descendant-v1");
        (await Schemas.GetCombinedSchemaAsync(b, Project, View, executionB.Annotation.ToString()))!.AppliedSchemas.Select(DescriptionOf)
            .Should().Equal("b-descendant-v1");

        var textName = JObject.Parse("""{ "name": "text" }""");
        var validateA = () => Schemas.ValidateContentAsync(a, Project, View, PaymentsKey, textName);
        var validateB = () => Schemas.ValidateContentAsync(b, Project, View, PaymentsKey, textName);
        await validateA.Should().NotThrowAsync();
        await validateB.Should().ThrowAsync<SchemaValidationException>();
    }

    [Fact]
    public async Task SchemaDeletes_LeaveTheSchemasOfTheOtherOrganization()
    {
        var (a, b) = Organizations("schema-deletes");

        foreach (var (organization, marker) in new[] { (a, "a"), (b, "b") })
        {
            await Schemas.SetSchemaAsync(organization, Project, View, AnnotationTypeCodes.Responsibility, Schema($"{marker}-type-v1"), Actor, false);
            await Schemas.SetAnnotationSchemaAsync(organization, Project, View, PaymentsKey, Schema($"{marker}-annotation-v1"), Actor, false);
            await Schemas.SetDescendantTypeSchemaAsync(organization, Project, View, PaymentsKey, AnnotationTypeCodes.Execution, Schema($"{marker}-descendant-v1"), Actor, false);
        }

        await Schemas.SetSchemaAsync(a, Project, View, AnnotationTypeCodes.Responsibility, Schema("a-type-v2"), Actor, false);
        var before = await SnapshotAsync(b);

        (await Schemas.DeleteSchemaAsync(a, Project, View, AnnotationTypeCodes.Responsibility)).Should().BeTrue();
        (await Schemas.DeleteAnnotationSchemaAsync(a, Project, View, PaymentsKey)).Should().BeTrue();
        (await Schemas.DeleteDescendantTypeSchemaAsync(a, Project, View, PaymentsKey, AnnotationTypeCodes.Execution)).Should().BeTrue();

        // A's documents are gone and a second delete finds nothing, although B has the same ids
        (await Schemas.GetSchemaAsync(a, Project, View, AnnotationTypeCodes.Responsibility)).Should().BeNull();
        (await Schemas.GetAnnotationSchemaAsync(a, Project, View, PaymentsKey)).Should().BeNull();
        (await Schemas.GetDescendantTypeSchemaAsync(a, Project, View, PaymentsKey, AnnotationTypeCodes.Execution)).Should().BeNull();
        (await Schemas.ListSchemasAsync(a, Project, View)).Should().BeEmpty();
        (await Schemas.DeleteSchemaAsync(a, Project, View, AnnotationTypeCodes.Responsibility)).Should().BeFalse();
        (await Schemas.DeleteAnnotationSchemaAsync(a, Project, View, PaymentsKey)).Should().BeFalse();

        (await SnapshotAsync(b)).Should().Equal(before);
        DescriptionOf(await Schemas.GetSchemaAsync(b, Project, View, AnnotationTypeCodes.Responsibility)).Should().Be("b-type-v1");
        DescriptionOf(await Schemas.GetAnnotationSchemaAsync(b, Project, View, PaymentsKey)).Should().Be("b-annotation-v1");
        DescriptionOf(await Schemas.GetDescendantTypeSchemaAsync(b, Project, View, PaymentsKey, AnnotationTypeCodes.Execution)).Should().Be("b-descendant-v1");
        (await Schemas.ListSchemasAsync(b, Project, View)).Should().HaveCount(3);

        // each organization continues its own version sequence: the state items share their ids too
        (await Schemas.SetSchemaAsync(a, Project, View, AnnotationTypeCodes.Responsibility, Schema("a-type-v3"), Actor, false)).Version.Should().Be(3);
        (await Schemas.SetSchemaAsync(b, Project, View, AnnotationTypeCodes.Responsibility, Schema("b-type-v2"), Actor, false)).Version.Should().Be(2);
        (await Schemas.GetSchemaVersionsAsync(a, Project, View, AnnotationTypeCodes.Responsibility)).Select(version => version.Version).Should().Equal(1u, 2u, 3u);
        (await Schemas.GetSchemaVersionsAsync(b, Project, View, AnnotationTypeCodes.Responsibility)).Select(version => version.Version).Should().Equal(1u, 2u);
    }

    // QueryAffectedConfigurations is organization-wide: B's configurations with the same ids, in the result, would fail
    // a save without force.
    [Fact]
    public async Task SchemaSaves_CheckOnlyTheConfigurationsOfTheirOrganization()
    {
        var (a, b) = Organizations("schema-compliance");
        var executionA = await CreateExecutionAsync(a);
        var executionB = await CreateExecutionAsync(b);
        var paymentsA = FullKey.Create(executionA.Annotation.GetResponsibilityKey(), executionA);
        var paymentsB = FullKey.Create(executionB.Annotation.GetResponsibilityKey(), executionB);

        // A's configurations comply with the strict schemas, B's violate them
        await Configs.CreateOrUpdateConfigurationAsync(paymentsA, JObject.Parse("""{ "name": "a-payments" }"""), Actor);
        await Configs.CreateOrUpdateConfigurationAsync(executionA, JObject.Parse("""{ "name": "a-execution" }"""), Actor);
        await Configs.CreateOrUpdateConfigurationAsync(paymentsB, JObject.Parse("""{ "badField": "b-payments" }"""), Actor);
        await Configs.CreateOrUpdateConfigurationAsync(executionB, JObject.Parse("""{ "badField": "b-execution" }"""), Actor);
        var before = await SnapshotAsync(b);

        // each kind queries differently: the type's ids, one annotation's id, the descendants of the annotation
        (await Schemas.SetSchemaAsync(a, Project, View, AnnotationTypeCodes.Responsibility, StrictNameSchema("a-type"), Actor, false))
            .Version.Should().Be(1);
        (await Schemas.SetAnnotationSchemaAsync(a, Project, View, PaymentsKey, StrictNameSchema("a-annotation"), Actor, false))
            .Version.Should().Be(1);
        (await Schemas.SetDescendantTypeSchemaAsync(a, Project, View, PaymentsKey, AnnotationTypeCodes.Execution, StrictNameSchema("a-descendant"), Actor, false))
            .Version.Should().Be(1);

        (await SnapshotAsync(b)).Should().Equal(before);
        (await Schemas.ListSchemasAsync(b, Project, View)).Should().BeEmpty();
        (await Configs.GetRawConfigurationAsync(paymentsB))!.Content["badField"]!.Value<string>().Should().Be("b-payments");
        (await Configs.GetRawConfigurationAsync(executionB))!.Content["badField"]!.Value<string>().Should().Be("b-execution");

        // the schemas are in force in A, and B's writes don't see them
        var violatingPaymentsA = () => Configs.CreateOrUpdateConfigurationAsync(paymentsA, JObject.Parse("""{ "badField": "a-again" }"""), Actor);
        var violatingExecutionA = () => Configs.CreateOrUpdateConfigurationAsync(executionA, JObject.Parse("""{ "badField": "a-again" }"""), Actor);
        await violatingPaymentsA.Should().ThrowAsync<SchemaValidationException>();
        await violatingExecutionA.Should().ThrowAsync<SchemaValidationException>();
        (await Configs.CreateOrUpdateConfigurationAsync(paymentsB, JObject.Parse("""{ "badField": "b-again" }"""), Actor))
            .Content["badField"]!.Value<string>().Should().Be("b-again");
        (await Configs.CreateOrUpdateConfigurationAsync(executionB, JObject.Parse("""{ "badField": "b-again" }"""), Actor))
            .Content["badField"]!.Value<string>().Should().Be("b-again");
    }

    [Fact]
    public async Task SchemaSaves_ReportOnlyTheViolationsOfTheirOrganization()
    {
        var (a, b) = Organizations("schema-violations");
        var executionA = await CreateExecutionAsync(a);
        var executionB = await CreateExecutionAsync(b);
        var paymentsA = FullKey.Create(executionA.Annotation.GetResponsibilityKey(), executionA);
        var paymentsB = FullKey.Create(executionB.Annotation.GetResponsibilityKey(), executionB);

        await Configs.CreateOrUpdateConfigurationAsync(paymentsA, JObject.Parse("""{ "badField": "a-payments" }"""), Actor);
        await Configs.CreateOrUpdateConfigurationAsync(executionA, JObject.Parse("""{ "badField": "a-execution" }"""), Actor);
        await Configs.CreateOrUpdateConfigurationAsync(paymentsB, JObject.Parse("""{ "badField": "b-payments" }"""), Actor);
        await Configs.CreateOrUpdateConfigurationAsync(executionB, JObject.Parse("""{ "badField": "b-execution" }"""), Actor);
        var before = await SnapshotAsync(b);

        // one violation each, A's own: B's configuration under the same id would be a second one
        var typeSave = () => Schemas.SetSchemaAsync(a, Project, View, AnnotationTypeCodes.Responsibility, StrictNameSchema("a-type"), Actor, false);
        var annotationSave = () => Schemas.SetAnnotationSchemaAsync(a, Project, View, PaymentsKey, StrictNameSchema("a-annotation"), Actor, false);
        var descendantSave = () => Schemas.SetDescendantTypeSchemaAsync(
            a, Project, View, PaymentsKey, AnnotationTypeCodes.Execution, StrictNameSchema("a-descendant"), Actor, false);

        (await typeSave.Should().ThrowAsync<SchemaValidationException>()).Which.ValidationErrors
            .Should().ContainSingle().Which.AnnotationKey.Should().Be(PaymentsKey);
        (await annotationSave.Should().ThrowAsync<SchemaValidationException>()).Which.ValidationErrors
            .Should().ContainSingle().Which.AnnotationKey.Should().Be(PaymentsKey);
        (await descendantSave.Should().ThrowAsync<SchemaValidationException>()).Which.ValidationErrors
            .Should().ContainSingle().Which.AnnotationKey.Should().Be(executionA.Annotation.ToString());

        // force stores them in A only
        (await Schemas.SetSchemaAsync(a, Project, View, AnnotationTypeCodes.Responsibility, StrictNameSchema("a-type"), Actor, true))
            .Version.Should().Be(1);
        (await Schemas.SetAnnotationSchemaAsync(a, Project, View, PaymentsKey, StrictNameSchema("a-annotation"), Actor, true))
            .Version.Should().Be(1);

        (await SnapshotAsync(b)).Should().Equal(before);
        (await Schemas.ListSchemasAsync(b, Project, View)).Should().BeEmpty();
        (await Schemas.ListSchemasAsync(a, Project, View)).Should().HaveCount(2);
    }

    [Fact]
    public async Task SchemaImportsAndHistories_FollowTheAnnotationsOfTheirOrganization()
    {
        var (a, b) = Organizations("schema-import");
        var executionA = await CreateExecutionAsync(a);
        var executionB = await CreateExecutionAsync(b);
        var paymentsA = FullKey.Create(executionA.Annotation.GetResponsibilityKey(), executionA);
        var paymentsB = FullKey.Create(executionB.Annotation.GetResponsibilityKey(), executionB);

        // rst.refunds exists in B only; B's configurations violate the strict schemas, A's comply
        var refundsB = await CreateResponsibilityAsync(b, RefundsName);
        await Configs.CreateOrUpdateConfigurationAsync(paymentsA, JObject.Parse("""{ "name": "a-payments" }"""), Actor);
        await Configs.CreateOrUpdateConfigurationAsync(paymentsB, JObject.Parse("""{ "badField": "b-payments" }"""), Actor);
        await Configs.CreateOrUpdateConfigurationAsync(refundsB, JObject.Parse("""{ "badField": "b-refunds" }"""), Actor);
        var before = await SnapshotAsync(b);
        var options = new ViewImportOptions { Actor = Actor };

        // the compliance check of an import sees A's configurations only
        var typeImport = await Schemas.ImportSchemaAsync(
            new ViewSchemaTarget(a, Project, View, ViewDocumentKind.TypeSchema, AnnotationTypeCodes.Responsibility, null),
            StrictNameSchema("a-type"),
            options,
            new ViewImportOverlay());
        typeImport.Outcome.Should().Be(ViewImportOutcome.Created);
        typeImport.Version.Should().Be(1UL);

        // an annotation schema needs its annotation in its own organization: B's rst.refunds doesn't count
        var refundsImport = await Schemas.ImportSchemaAsync(
            new ViewSchemaTarget(a, Project, View, ViewDocumentKind.AnnotationSchema, null, RefundsKey),
            StrictNameSchema("a-refunds"),
            options,
            new ViewImportOverlay());
        refundsImport.Outcome.Should().Be(ViewImportOutcome.Skipped);
        refundsImport.ErrorCode.Should().Be(ErrorCodes.AnnotationNotFound);

        // a schema write doesn't ask for its annotation (and checks B's violating rst.refunds on a leak); the export
        // leaves out a schema whose annotation A doesn't have, even though B has it
        await Schemas.SetAnnotationSchemaAsync(a, Project, View, RefundsKey, StrictNameSchema("a-refunds"), Actor, false);
        await Schemas.SetAnnotationSchemaAsync(a, Project, View, PaymentsKey, StrictNameSchema("a-payments"), Actor, false);

        (await Schemas.GetSchemaKeysAsync(a, Project, View, TestContext.Current.CancellationToken)).Should().BeEquivalentTo(new[]
        {
            SchemaKey.ForType(AnnotationTypeCodes.Responsibility),
            SchemaKey.ForAnnotation(PaymentsKey),
            SchemaKey.ForAnnotation(RefundsKey),
        });
        (await Schemas.GetSchemaHistoriesAsync(a, Project, View, TestContext.Current.CancellationToken))
            .Select(history => (history.Kind, history.AnnotationType, history.AnnotationKey))
            .Should().Equal(
                (ViewDocumentKind.TypeSchema, AnnotationTypeCodes.Responsibility, null),
                (ViewDocumentKind.AnnotationSchema, null, PaymentsKey));

        (await SnapshotAsync(b)).Should().Equal(before);
        (await Schemas.GetSchemaKeysAsync(b, Project, View, TestContext.Current.CancellationToken)).Should().BeEmpty();

        // B's own import is checked against B's configurations, both of them (a dry run writes nothing)
        var importB = await Schemas.ImportSchemaAsync(
            new ViewSchemaTarget(b, Project, View, ViewDocumentKind.TypeSchema, AnnotationTypeCodes.Responsibility, null),
            StrictNameSchema("b-type"),
            options with { DryRun = true },
            new ViewImportOverlay());
        importB.Outcome.Should().Be(ViewImportOutcome.Failed);
        importB.ErrorCode.Should().Be(ErrorCodes.SchemaViolation);
        importB.Message.Should().Be($"The configurations of {PaymentsKey}, {RefundsKey} violate this schema.");
    }

    [Fact]
    public async Task TemplateReads_SeeOnlyTheTemplatesOfTheirOrganization()
    {
        var (a, b) = Organizations("template-reads");

        // A has two versions of the execution template and one in another view, B a subject template that A doesn't have
        await Templates.CreateOrUpdateTemplateAsync(a, Project, View, AnnotationTypeCodes.Execution, OwnerDocument("a-v1"), Actor);
        await Templates.CreateOrUpdateTemplateAsync(a, Project, View, AnnotationTypeCodes.Execution, OwnerDocument("a-v2"), Actor);
        await Templates.CreateOrUpdateTemplateAsync(a, Project, OtherView, AnnotationTypeCodes.Execution, OwnerDocument("a-prod-v1"), Actor);
        await Templates.CreateOrUpdateTemplateAsync(b, Project, View, AnnotationTypeCodes.Execution, OwnerDocument("b-v1"), Actor);
        await Templates.CreateOrUpdateTemplateAsync(b, Project, View, AnnotationTypeCodes.Subject, OwnerDocument("b-subject-v1"), Actor);

        var templateA = await Templates.GetTemplateAsync(a, Project, View, AnnotationTypeCodes.Execution);
        OwnerOf(templateA).Should().Be("a-v2");
        templateA!.Version.Should().Be(2);
        OwnerOf(await Templates.GetTemplateAsync(b, Project, View, AnnotationTypeCodes.Execution)).Should().Be("b-v1");
        (await Templates.GetTemplateAsync(a, Project, View, AnnotationTypeCodes.Subject)).Should().BeNull();
        (await Templates.GetTemplateAsync(b, Project, OtherView, AnnotationTypeCodes.Execution)).Should().BeNull();

        (await Templates.ListTemplatesAsync(a, Project, View))
            .Select(summary => (summary.AnnotationType, summary.Version))
            .Should().Equal((AnnotationTypeCodes.Execution, 2UL));
        (await Templates.ListTemplatesAsync(b, Project, View))
            .Select(summary => (summary.AnnotationType, summary.Version))
            .Should().Equal((AnnotationTypeCodes.Execution, 1UL), (AnnotationTypeCodes.Subject, 1UL));
        (await Templates.ListTemplatesAsync(b, Project, OtherView)).Should().BeEmpty();

        (await Templates.GetTemplateVersionsAsync(a, Project, View, AnnotationTypeCodes.Execution)).Select(version => version.Version).Should().Equal(1u, 2u);
        (await Templates.GetTemplateVersionsAsync(b, Project, View, AnnotationTypeCodes.Execution)).Select(version => version.Version).Should().Equal(1u);
        (await Templates.GetTemplateVersionsAsync(a, Project, View, AnnotationTypeCodes.Subject)).Should().BeEmpty();
        OwnerOf(await Templates.GetTemplateVersionContentAsync(a, Project, View, AnnotationTypeCodes.Execution, 1)).Should().Be("a-v1");
        OwnerOf(await Templates.GetTemplateVersionContentAsync(b, Project, View, AnnotationTypeCodes.Execution, 1)).Should().Be("b-v1");
        (await Templates.GetTemplateVersionContentAsync(b, Project, View, AnnotationTypeCodes.Execution, 2)).Should().BeNull();

        // a version that only A has is unknown to B
        (await Templates.GetTemplateVersionChangesAsync(a, Project, View, AnnotationTypeCodes.Execution, 2)).Stats.Additions.Should().BeGreaterThan(0);
        var changesB = () => Templates.GetTemplateVersionChangesAsync(b, Project, View, AnnotationTypeCodes.Execution, 2);
        await changesB.Should().ThrowAsync<InvalidOperationException>();

        // the histories of an export, oldest first, by type
        (await Templates.GetTemplateHistoriesAsync(a, Project, View, TestContext.Current.CancellationToken))
            .SelectMany(history => history.Versions.Select(document => (history.AnnotationType, document.Content["owner"]!.Value<string>())))
            .Should().Equal((AnnotationTypeCodes.Execution, "a-v1"), (AnnotationTypeCodes.Execution, "a-v2"));
        (await Templates.GetTemplateHistoriesAsync(b, Project, View, TestContext.Current.CancellationToken))
            .SelectMany(history => history.Versions.Select(document => (history.AnnotationType, document.Content["owner"]!.Value<string>())))
            .Should().Equal((AnnotationTypeCodes.Execution, "b-v1"), (AnnotationTypeCodes.Subject, "b-subject-v1"));
        (await Templates.GetTemplateHistoriesAsync(b, Project, OtherView, TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task TemplateDeletes_LeaveTheTemplatesOfTheOtherOrganization()
    {
        var (a, b) = Organizations("template-deletes");
        await Templates.CreateOrUpdateTemplateAsync(a, Project, View, AnnotationTypeCodes.Execution, OwnerDocument("a-v1"), Actor);
        await Templates.CreateOrUpdateTemplateAsync(a, Project, View, AnnotationTypeCodes.Execution, OwnerDocument("a-v2"), Actor);
        await Templates.CreateOrUpdateTemplateAsync(b, Project, View, AnnotationTypeCodes.Execution, OwnerDocument("b-v1"), Actor);
        var before = await SnapshotAsync(b);

        (await Templates.DeleteTemplateAsync(a, Project, View, AnnotationTypeCodes.Execution)).Should().BeTrue();

        // A's template is gone and a second delete or a patch finds nothing, although B has the same ids
        (await Templates.GetTemplateAsync(a, Project, View, AnnotationTypeCodes.Execution)).Should().BeNull();
        (await Templates.ListTemplatesAsync(a, Project, View)).Should().BeEmpty();
        (await Templates.DeleteTemplateAsync(a, Project, View, AnnotationTypeCodes.Execution)).Should().BeFalse();
        var patch = () => Templates.PatchTemplateAsync(
            a,
            Project,
            View,
            AnnotationTypeCodes.Execution,
            JArray.Parse("""[ { "op": "replace", "path": "/owner", "value": "a-patched" } ]"""),
            Actor);
        await patch.Should().ThrowAsync<TemplateNotFoundException>();

        (await SnapshotAsync(b)).Should().Equal(before);
        OwnerOf(await Templates.GetTemplateAsync(b, Project, View, AnnotationTypeCodes.Execution)).Should().Be("b-v1");

        // each organization continues its own version sequence: the state items share their ids too
        (await Templates.CreateOrUpdateTemplateAsync(a, Project, View, AnnotationTypeCodes.Execution, OwnerDocument("a-v3"), Actor)).Version.Should().Be(3);
        (await Templates.CreateOrUpdateTemplateAsync(b, Project, View, AnnotationTypeCodes.Execution, OwnerDocument("b-v2"), Actor)).Version.Should().Be(2);
        (await Templates.GetTemplateVersionsAsync(a, Project, View, AnnotationTypeCodes.Execution)).Select(version => version.Version).Should().Equal(1u, 2u, 3u);
        (await Templates.GetTemplateVersionsAsync(b, Project, View, AnnotationTypeCodes.Execution)).Select(version => version.Version).Should().Equal(1u, 2u);
    }

    // Every template write runs the organization-wide query of ConfigurationCacheInvalidator: B's cached calculations
    // under the same ids must keep their content, hash and counter.
    [Fact]
    public async Task TemplateWrites_InvalidateAndMergeOnlyInTheirOrganization()
    {
        var (a, b) = Organizations("template-invalidation");
        var executionA = await CreateExecutionAsync(a);
        var executionB = await CreateExecutionAsync(b);
        await Configs.CreateOrUpdateConfigurationAsync(executionA, JObject.Parse("""{ "retries": "a-retries" }"""), Actor);
        await Configs.CreateOrUpdateConfigurationAsync(executionB, JObject.Parse("""{ "retries": "b-retries" }"""), Actor);
        await Templates.CreateOrUpdateTemplateAsync(a, Project, View, AnnotationTypeCodes.Execution, OwnerDocument("a-owner"), Actor);
        await Templates.CreateOrUpdateTemplateAsync(b, Project, View, AnnotationTypeCodes.Execution, OwnerDocument("b-owner"), Actor);

        // each merges its own template, and both calculations are cached now
        var configurationA = await Configs.GetRawConfigurationAsync(executionA);
        var configurationB = await Configs.GetRawConfigurationAsync(executionB);
        configurationA!.Content["owner"]!.Value<string>().Should().Be("a-owner");
        configurationA.Content["retries"]!.Value<string>().Should().Be("a-retries");
        configurationB!.Content["owner"]!.Value<string>().Should().Be("b-owner");
        configurationB.Content["retries"]!.Value<string>().Should().Be("b-retries");
        (await HasCachedCalculationAsync(executionA)).Should().BeTrue();
        (await HasCachedCalculationAsync(executionB)).Should().BeTrue();
        var before = await SnapshotAsync(b);

        // a write, a patch, a template of an ancestor type (invalidates across the partitions), a delete and an import
        await Templates.CreateOrUpdateTemplateAsync(a, Project, View, AnnotationTypeCodes.Execution, OwnerDocument("a-owner-2"), Actor);
        (await HasCachedCalculationAsync(executionA)).Should().BeFalse();
        await Templates.PatchTemplateAsync(
            a,
            Project,
            View,
            AnnotationTypeCodes.Execution,
            JArray.Parse("""[ { "op": "replace", "path": "/owner", "value": "a-owner-3" } ]"""),
            Actor);
        await Templates.CreateOrUpdateTemplateAsync(a, Project, View, AnnotationTypeCodes.Responsibility, JObject.Parse("""{ "tier": "a-gold" }"""), Actor);
        (await Templates.DeleteTemplateAsync(a, Project, View, AnnotationTypeCodes.Execution)).Should().BeTrue();
        var imported = await Templates.ImportTemplateAsync(
            a,
            Project,
            View,
            AnnotationTypeCodes.Execution,
            OwnerDocument("a-imported"),
            new ViewImportOptions { Actor = Actor });
        imported.Outcome.Should().Be(ViewImportOutcome.Created);
        imported.Version.Should().Be(4UL);

        (await SnapshotAsync(b)).Should().Equal(before);
        (await HasCachedCalculationAsync(executionB)).Should().BeTrue();

        var finalA = await Configs.GetRawConfigurationAsync(executionA);
        finalA!.Content["owner"]!.Value<string>().Should().Be("a-imported");
        finalA.Content["tier"]!.Value<string>().Should().Be("a-gold");
        finalA.Content["retries"]!.Value<string>().Should().Be("a-retries");
        var finalB = await Configs.GetRawConfigurationAsync(executionB);
        finalB!.Content["owner"]!.Value<string>().Should().Be("b-owner");
        finalB.Content.Should().NotContainKey("tier");
        finalB.Content["retries"]!.Value<string>().Should().Be("b-retries");
        OwnerOf(await Templates.GetTemplateAsync(b, Project, View, AnnotationTypeCodes.Execution)).Should().Be("b-owner");
        (await Templates.GetTemplateAsync(b, Project, View, AnnotationTypeCodes.Responsibility)).Should().BeNull();

        // B's next write continues B's sequence, and invalidates B's calculation
        (await Templates.CreateOrUpdateTemplateAsync(b, Project, View, AnnotationTypeCodes.Execution, OwnerDocument("b-owner-2"), Actor)).Version.Should().Be(2);
        (await HasCachedCalculationAsync(executionB)).Should().BeFalse();
        (await Configs.GetRawConfigurationAsync(executionB))!.Content["owner"]!.Value<string>().Should().Be("b-owner-2");
    }

    // Names valid by NameRules, unique per test, so the tests of the class keep apart in the shared collection.
    private static (string A, string B) Organizations(string test)
    {
        return ($"iso-{test}-a", $"iso-{test}-b");
    }

    // A schema whose description names its organization and version; with nameType, it also types the property name.
    private static JObject Schema(string description, string? nameType = null)
    {
        var schema = new JObject
        {
            ["type"] = "object",
            ["description"] = description,
        };

        if (nameType is not null)
        {
            schema["properties"] = new JObject { ["name"] = new JObject { ["type"] = nameType } };
        }

        return schema;
    }

    // Only a string "name": a configuration with any other property violates it.
    private static JObject StrictNameSchema(string description)
    {
        return new JObject
        {
            ["type"] = "object",
            ["description"] = description,
            ["properties"] = new JObject { ["name"] = new JObject { ["type"] = "string" } },
            ["required"] = new JArray("name"),
            ["additionalProperties"] = false,
        };
    }

    private static string? DescriptionOf(ConfigurationSchema? schema)
    {
        return schema?.Content["description"]?.Value<string>();
    }

    private static JObject OwnerDocument(string owner)
    {
        return new JObject { ["owner"] = owner };
    }

    private static string? OwnerOf(ConfigurationTemplate? template)
    {
        return template?.Content["owner"]?.Value<string>();
    }

    private OrganizationContainer Repository(string organization)
    {
        return Context.Services
            .GetRequiredService<IContainerRepositoryProvider>()
            .GetOrganizationContainer(organization);
    }

    // Every item of the organization as stored, its system properties included: any write that reached one of them,
    // also a patch that clears a cached calculation, changes at least its _etag and _ts.
    private async Task<Dictionary<string, string>> SnapshotAsync(string organization)
    {
        var repository = Repository(organization);
        var query = repository.WithOrganization(
            new QueryDefinition($"SELECT * FROM c WHERE c.Organization = {OrganizationContainer.OrganizationParameter}"));
        using var iterator = repository.Container.GetItemQueryIterator<JObject>(query, requestOptions: repository.QueryOptions());
        var items = new Dictionary<string, string>(StringComparer.Ordinal);

        while (iterator.HasMoreResults)
        {
            foreach (var item in await iterator.ReadNextAsync(TestContext.Current.CancellationToken))
            {
                items.Add($"{item["PartitionKey"]}|{item["id"]}", item.ToString(Formatting.None));
            }
        }

        // the organization has data, so an unchanged snapshot is not an empty one
        items.Should().NotBeEmpty();
        return items;
    }

    private async Task<bool> HasCachedCalculationAsync(FullKey key)
    {
        var item = await Repository(key.OrganizationName).Container.ReadItemAsync<JObject>(
            $"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{key.Annotation}",
            key.GetCosmosPartitionKey(),
            cancellationToken: TestContext.Current.CancellationToken);

        return item.Resource["CalculatedContent"]?.Type == JTokenType.Object;
    }

    // exe.customer.payments.live; creating it creates its ancestors, rst.payments among them
    private async Task<FullKey> CreateExecutionAsync(string organization)
    {
        var executionKey = AnnotationKey.CreateExecution(SubjectName, ResponsibilityName, ContextName);
        await Annotations.CreateAnnotationAsync(organization, new Execution
        {
            AnnotationKey = executionKey,
            AnnotationType = AnnotationType.Execution,
            Name = ContextName,
            SubjectKey = AnnotationKey.CreateSubject(SubjectName),
            SubjectName = SubjectName,
            ResponsibilityKey = AnnotationKey.CreateResponsibility(ResponsibilityName),
            ResponsibilityName = ResponsibilityName,
            ContextKey = AnnotationKey.CreateContext(SubjectName, ContextName),
            ContextName = ContextName,
            UsageKey = AnnotationKey.CreateUsage(SubjectName, ResponsibilityName),
            ProjectName = Project,
            ViewName = View,
        });

        return FullKey.Create(executionKey, organization, Project, View);
    }

    private async Task<FullKey> CreateResponsibilityAsync(string organization, string name)
    {
        var annotationKey = AnnotationKey.CreateResponsibility(name);
        await Annotations.CreateAnnotationAsync(organization, new Responsibility
        {
            AnnotationKey = annotationKey,
            AnnotationType = AnnotationType.Responsibility,
            Name = name,
            ProjectName = Project,
            ViewName = View,
        });

        return FullKey.Create(annotationKey, organization, Project, View);
    }
}
