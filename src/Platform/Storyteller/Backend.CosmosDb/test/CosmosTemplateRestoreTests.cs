using System;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The restore of a template version: a new version with the old content, the no-ops, a deleted template, the
// numbering and the cache of the calculated configurations. Every test works in its own project.
public class CosmosTemplateRestoreTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string ProjectPrefix = "tpl-restore";
    private const string View = Constants.DefaultViewName;
    private const string Type = AnnotationTypeCodes.Execution;

    private static readonly AuditActor Editor = AuditActor.Account("user_editor", "Editor");

    private IConfigurationTemplateService Templates => Context.Services.GetRequiredService<IConfigurationTemplateService>();

    private IConfigurationService Configs => Context.Services.GetRequiredService<IConfigurationService>();

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    [Fact]
    public async Task Restore_OfAnOldVersion_StoresItWholeAsANewVersion()
    {
        var project = $"{ProjectPrefix}-old";
        await WriteAsync(project, """{ "owner": "platform", "tier": 1 }""");
        await WriteAsync(project, """{ "owner": "team" }""");

        var result = await Templates.RestoreTemplateVersionAsync(TestConstants.Organization, project, View, "EXE", 1, Editor);

        result.Outcome.Should().Be(VersionRestoreOutcome.Restored);
        result.FromVersion.Should().Be(1);
        result.Version.Should().Be(3);
        result.Modified.By.Id.Should().Be(Editor.Id);

        var stored = await Templates.GetTemplateAsync(TestConstants.Organization, project, View, Type);
        JToken.DeepEquals(stored!.Content, JObject.Parse("""{ "owner": "platform", "tier": 1 }""")).Should().BeTrue("a restore replaces, it does not merge: {0}", stored.Content);

        var versions = await Templates.GetTemplateVersionsAsync(TestConstants.Organization, project, View, Type);
        versions.Select(version => version.Version).Should().Equal(1u, 2u, 3u);
        versions.Select(version => version.RestoredFrom).Should().Equal(null, null, 1u);

        await WriteAsync(project, """{ "region": "eu" }""");
        versions = await Templates.GetTemplateVersionsAsync(TestConstants.Organization, project, View, Type);
        versions.Select(version => version.RestoredFrom).Should().Equal(null, null, 1u, null);
    }

    [Fact]
    public async Task Restore_OfTheCurrentVersion_OrOfOneItEquals_ChangesNothing()
    {
        var project = $"{ProjectPrefix}-unchanged";
        await WriteAsync(project, """{ "owner": "platform" }""");
        await WriteAsync(project, """{ "owner": "team" }""");
        await WriteAsync(project, """{ "owner": "platform" }""");

        var current = await Templates.RestoreTemplateVersionAsync(TestConstants.Organization, project, View, Type, 3, Editor);
        var equal = await Templates.RestoreTemplateVersionAsync(TestConstants.Organization, project, View, Type, 1, Editor);

        current.Outcome.Should().Be(VersionRestoreOutcome.Unchanged);
        current.Version.Should().Be(3);
        equal.Outcome.Should().Be(VersionRestoreOutcome.Unchanged);
        equal.FromVersion.Should().Be(1);
        equal.Version.Should().Be(3);
        equal.Modified.By.Id.Should().Be(AuditActor.System.Id);
        (await Templates.GetTemplateVersionsAsync(TestConstants.Organization, project, View, Type)).Select(version => version.Version).Should().Equal(1u, 2u, 3u);
    }

    [Fact]
    public async Task Restore_OfADeletedTemplate_CreatesItAgain_AfterTheLastVersion()
    {
        var project = $"{ProjectPrefix}-deleted";
        await WriteAsync(project, """{ "owner": "platform" }""");
        await WriteAsync(project, """{ "owner": "team" }""");
        (await Templates.DeleteTemplateAsync(TestConstants.Organization, project, View, Type)).Should().BeTrue();

        var result = await Templates.RestoreTemplateVersionAsync(TestConstants.Organization, project, View, Type, 2, Editor);

        result.Outcome.Should().Be(VersionRestoreOutcome.Restored, "a deleted template has no current version");
        result.Version.Should().Be(3);
        (await Templates.GetTemplateAsync(TestConstants.Organization, project, View, Type))!.Content["owner"]!.Value<string>().Should().Be("team");
        var versions = await Templates.GetTemplateVersionsAsync(TestConstants.Organization, project, View, Type);
        versions.Select(version => version.Version).Should().Equal(1u, 2u, 3u);
        versions.Last().RestoredFrom.Should().Be(2);
    }

    [Fact]
    public async Task Restore_OfAnUnknownOrExpiredVersion_IsVersionNotFound()
    {
        var project = $"{ProjectPrefix}-unknown";
        await WriteAsync(project, """{ "owner": "platform" }""");
        await WriteAsync(project, """{ "owner": "team" }""");

        await ShouldBeVersionNotFoundAsync(project, 9);
        await ShouldBeVersionNotFoundAsync($"{ProjectPrefix}-never", 1);

        // an expired history item is gone from the container
        await GetContainer().DeleteItemAsync<JObject>($"{View}.gnv.{Type}.1", PartitionKeys.GetCosmosTemplate(TestConstants.Organization, project));
        await ShouldBeVersionNotFoundAsync(project, 1);
    }

    [Fact]
    public async Task Restore_ClearsTheCalculatedConfigurations()
    {
        var project = $"{ProjectPrefix}-invalidate";
        var execution = await CreateExecutionAsync(project);
        await WriteAsync(project, """{ "owner": "platform" }""");
        await WriteAsync(project, """{ "owner": "team" }""");
        (await Configs.GetRawConfigurationAsync(execution))!.Content["owner"]!.Value<string>().Should().Be("team");

        await Templates.RestoreTemplateVersionAsync(TestConstants.Organization, project, View, Type, 1, Editor);

        (await Configs.GetRawConfigurationAsync(execution))!.Content["owner"]!.Value<string>().Should().Be("platform");
    }

    [Fact]
    public async Task Restore_CopiesTheStoredContentAsItIs()
    {
        var project = $"{ProjectPrefix}-verbatim";
        await WriteAsync(project, """{ "owner": "platform" }""");

        // a JSON Patch stores a literal "$patch", which a write would read as a directive
        await Templates.PatchTemplateAsync(TestConstants.Organization, project, View, Type, JArray.Parse("""[{ "op": "add", "path": "/$patch", "value": [] }]"""), AuditActor.System);
        await Templates.PatchTemplateAsync(TestConstants.Organization, project, View, Type, JArray.Parse("""[{ "op": "replace", "path": "/owner", "value": "team" }]"""), AuditActor.System);

        await Templates.RestoreTemplateVersionAsync(TestConstants.Organization, project, View, Type, 2, Editor);

        var stored = (await Templates.GetTemplateAsync(TestConstants.Organization, project, View, Type))!.Content;
        JToken.DeepEquals(stored, JObject.Parse("""{ "owner": "platform", "$patch": [] }""")).Should().BeTrue("the stored version is copied as it is: {0}", stored);
    }

    [Fact]
    public async Task Restore_RetriesAConcurrentChange_AndGivesUpAfterThreeMore()
    {
        var project = $"{ProjectPrefix}-concurrent";
        await WriteAsync(project, """{ "owner": "platform" }""");
        await WriteAsync(project, """{ "owner": "team" }""");
        var storage = new InterceptedRepositoryProvider(Context.Services.GetRequiredService<IContainerRepositoryProvider>());
        var templates = ActivatorUtilities.CreateInstance<CosmosConfigurationTemplateService>(Context.Services, storage);
        var id = $"{View}.gen.{Type}";
        var changes = 0;
        var always = false;

        storage.Interceptor.BeforeBatch = async batch =>
        {
            if (batch.Contains(BatchOperationKind.Replace, id) && (always || changes++ == 0))
            {
                await TouchAsync(project, id);
            }

            return null;
        };

        var restored = await templates.RestoreTemplateVersionAsync(TestConstants.Organization, project, View, Type, 1, Editor);

        changes.Should().Be(2, "the first attempt lost to the concurrent change, the second wrote");
        restored.Version.Should().Be(3);

        always = true;
        var act = () => templates.RestoreTemplateVersionAsync(TestConstants.Organization, project, View, Type, 2, Editor);

        await act.Should().ThrowAsync<TemplateConcurrencyException>();
        (await Templates.GetTemplateAsync(TestConstants.Organization, project, View, Type))!.Version.Should().Be(3);
    }

    // A change of the item that leaves its document as it is.
    private async Task TouchAsync(string project, string id)
    {
        using var response = await GetContainer().PatchItemStreamAsync(
            id,
            PartitionKeys.GetCosmosTemplate(TestConstants.Organization, project),
            [PatchOperation.Set("/touched", Guid.NewGuid().ToString("N"))]);

        response.IsSuccessStatusCode.Should().BeTrue();
    }

    private Task WriteAsync(string project, string json)
    {
        return Templates.CreateOrUpdateTemplateAsync(TestConstants.Organization, project, View, Type, JObject.Parse(json), AuditActor.System);
    }

    private async Task ShouldBeVersionNotFoundAsync(string project, uint version)
    {
        var act = () => Templates.RestoreTemplateVersionAsync(TestConstants.Organization, project, View, Type, version, Editor);
        (await act.Should().ThrowAsync<NotFoundException>()).Which.ErrorCode.Should().Be(ErrorCodes.VersionNotFound);
    }

    private Container GetContainer()
    {
        return Context.Services
            .GetRequiredService<IContainerRepositoryProvider>()
            .GetOrganizationContainer(TestConstants.Organization)
            .Container;
    }

    private async Task<FullKey> CreateExecutionAsync(string project)
    {
        var executionKey = AnnotationKey.CreateExecution("customer", "billing", "prod");
        await Annotations.CreateAnnotationsFromStringAsync(TestConstants.Organization, project, View, [executionKey.ToString()], AnnotationServiceTestExtensions.TestActor);
        return FullKey.Create(executionKey, TestConstants.Organization, project, View);
    }
}
