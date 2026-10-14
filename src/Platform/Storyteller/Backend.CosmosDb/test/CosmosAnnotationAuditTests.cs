using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The audit document of annotations: entries with previous and new values, the stamps, and the size limit.
public class CosmosAnnotationAuditTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Project = "audit";

    private static readonly AuditActor Editor = new() { Kind = AuditActorKind.Account, Id = "user_editor", Name = "Editor" };

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    private Container Container => Context.Services
        .GetRequiredService<IContainerRepositoryProvider>()
        .GetOrganizationContainer(TestConstants.Organization)
        .Container;

    [Fact]
    public async Task Update_RecordsThePreviousAndTheNewValues()
    {
        await Annotations.CreateAnnotationAsync(TestConstants.Organization, Model("sbt.au-s") with
        {
            Title = "One",
            Labels = ["eu"],
            Values = new Dictionary<string, object> { ["plan"] = "pro", ["settings"] = new { mode = "a" } },
        });

        var updated = await Annotations.SetAnnotationAsync(
            TestConstants.Organization,
            Model("sbt.au-s") with
            {
                Title = "Two",
                Description = "never in the audit",
                Labels = ["eu", "us"],
                Values = new Dictionary<string, object> { ["plan"] = "enterprise", ["settings"] = new { mode = "b" } },
            },
            Editor);

        updated.Title.Should().Be("Two");
        updated.Modified!.By.Id.Should().Be(Editor.Id);
        updated.Created!.By.Id.Should().Be(AnnotationServiceTestExtensions.TestActor.Id);

        var history = await Annotations.GetAnnotationHistoryAsync(Key("sbt.au-s"));
        history!.State.Should().Be(AnnotationState.Active);
        history.Entries.Select(e => e.Action).Should().Equal(AnnotationAuditAction.Updated, AnnotationAuditAction.Created);

        var update = history.Entries[0];
        update.Actor.Id.Should().Be(Editor.Id);
        update.Actor.Name.Should().Be("Editor");

        var title = update.Fields!.Single(f => f.Field == "Title");
        title.Previous!.ToString().Should().Be("One");
        title.Value!.ToString().Should().Be("Two");

        var description = update.Fields!.Single(f => f.Field == "Description");
        description.Previous.Should().BeNull();
        description.Value.Should().BeNull();

        update.Labels!.Added.Should().Equal("us");
        update.Labels.Removed.Should().BeEmpty();

        var plan = update.Values!.Single(v => v.Key == "plan");
        plan.Previous!.ToString().Should().Be("pro");
        plan.Value!.ToString().Should().Be("enterprise");

        var settings = update.Values!.Single(v => v.Key == "settings");
        settings.PreviousKind.Should().Be(AnnotationValueKind.Object);
        settings.Previous.Should().BeNull();
    }

    [Fact]
    public async Task Update_WithoutChange_WritesNothing()
    {
        var annotation = Model("sbt.an-s") with { Title = "Same" };
        await Annotations.CreateAnnotationAsync(TestConstants.Organization, annotation);

        await Annotations.SetAnnotationAsync(TestConstants.Organization, annotation, Editor);

        var history = await Annotations.GetAnnotationHistoryAsync(Key("sbt.an-s"));
        history!.Entries.Should().ContainSingle(e => e.Action == AnnotationAuditAction.Created);
    }

    [Fact]
    public async Task Update_KeepsTheArraysTheServerOwns()
    {
        await Annotations.CreateAnnotationAsync(TestConstants.Organization, Model("usg.ua-s.ua-r"));

        await Annotations.SetAnnotationAsync(TestConstants.Organization, Model("sbt.ua-s") with { Title = "Body without arrays" }, Editor);

        var subject = (Subject)(await Annotations.GetAnnotationAsync(Key("sbt.ua-s")))!;
        subject.ResponsibilityNames.Should().Contain("ua-r");
        subject.Title.Should().Be("Body without arrays");
    }

    [Fact]
    public async Task Create_RecordsTheAncestorsAsCreatedForTheDescendant()
    {
        await Annotations.CreateAnnotationAsync(TestConstants.Organization, Model("cnt.ac-s.ac-c"));

        var history = await Annotations.GetAnnotationHistoryAsync(Key("sbt.ac-s"));
        var created = history!.Entries.Should().ContainSingle().Subject;
        created.Action.Should().Be(AnnotationAuditAction.Created);
        created.Cause!.Kind.Should().Be(AnnotationAuditCauseKind.Ancestor);
        created.Cause.Key.Should().Be("cnt.ac-s.ac-c");

        var context = await Annotations.GetAnnotationHistoryAsync(Key("cnt.ac-s.ac-c"));
        context!.Entries.Should().ContainSingle(e => e.Action == AnnotationAuditAction.Created && e.Cause == null);
    }

    [Fact]
    public async Task ConfigurationChange_IsInTheHistory()
    {
        var configurations = Context.Services.GetRequiredService<IConfigurationService>();
        await Annotations.CreateAnnotationAsync(TestConstants.Organization, Model("sbt.cc-s"));

        await configurations.CreateOrUpdateConfigurationAsync(Key("sbt.cc-s"), JObject.Parse("""{ "a": 1 }"""), Editor);
        await configurations.CreateOrUpdateConfigurationAsync(Key("sbt.cc-s"), JObject.Parse("""{ "a": 2 }"""), AuditActor.System);

        var history = await Annotations.GetAnnotationHistoryAsync(Key("sbt.cc-s"));
        history!.Entries.Should().HaveCount(3);
        history.Entries[0].Action.Should().Be(AnnotationAuditAction.ConfigurationChanged);
        history.Entries[0].ConfigurationVersion.Should().Be(2);
        history.Entries[0].Actor.Should().Be(AuditActor.System);
        history.Entries[1].Action.Should().Be(AnnotationAuditAction.ConfigurationChanged);
        history.Entries[1].ConfigurationVersion.Should().Be(1);
        history.Entries[1].Actor.Id.Should().Be(Editor.Id);
    }

    [Fact]
    public async Task SeededAnnotation_IsStampedByTheSystem()
    {
        var key = FullKey.Create(AnnotationKey.CreateResponsibility("storyteller"), TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName);

        var seeded = await Annotations.GetAnnotationAsync(key);

        seeded!.Created!.By.Kind.Should().Be(AuditActorKind.System);
        seeded.Modified!.By.Id.Should().Be(AuditActor.System.Id);
    }

    [Fact]
    public async Task History_OfUnknownKey_IsNull()
    {
        (await Annotations.GetAnnotationHistoryAsync(Key("sbt.hu-never"))).Should().BeNull();
    }

    [Fact]
    public async Task Audit_IsTrimmedToItsSizeLimit()
    {
        var configuration = new ConfigurationBuilder()
            .AddConfiguration(Context.Configuration)
            .Add(new MemoryConfigurationSource
            {
                InitialData = [new KeyValuePair<string, string>("Audit:MaxDocumentBytes", "4096")],
            })
            .Build();

        await using var services = new ServiceCollection()
            .AddCosmosDbAnnotations(configuration)
            .BuildServiceProvider();
        var annotations = services.GetRequiredService<IAnnotationService>();

        await annotations.CreateAnnotationAsync(TestConstants.Organization, Model("sbt.tr-s"), Editor);

        for (var index = 0; index < 40; index++)
        {
            await annotations.SetAnnotationAsync(TestConstants.Organization, Model("sbt.tr-s") with { Title = $"Title {index} " + new string('x', 100) }, Editor);
        }

        var history = await annotations.GetAnnotationHistoryAsync(Key("sbt.tr-s"));
        history!.IsTrimmed.Should().BeTrue();
        history.Entries.Count.Should().BeLessThan(41);
        history.Entries[0].Fields!.Single(f => f.Field == "Title").Value!.ToString().Should().StartWith("Title 39 ");

        using var response = await Container.ReadItemStreamAsync("default.adt.sbt.tr-s", new PartitionKey($"{Project}.sbt.tr-s"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var stored = response.Content.Length;
        using var document = await JsonDocument.ParseAsync(response.Content);
        var sizeInBytes = document.RootElement.GetProperty("SizeInBytes").GetInt32();

        sizeInBytes.Should().BeLessThanOrEqualTo(4096 - AnnotationAuditStore.SystemPropertiesAllowance);
        stored.Should().BeLessThanOrEqualTo(4096);
        stored.Should().BeGreaterThan(sizeInBytes, "Cosmos adds its system properties to the stored item");
    }

    private static FullKey Key(string key)
    {
        return FullKey.Create(AnnotationKey.Parse(key), TestConstants.Organization, Project, Constants.DefaultViewName);
    }

    private static Annotation Model(string key)
    {
        return AnnotationFactory.CreateDefault(AnnotationKey.Parse(key), Project, Constants.DefaultViewName);
    }
}
