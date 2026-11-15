using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Entities.Access;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.Audit;
using _42.Platform.Storyteller.KeyValues.Policy;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

using Permission = _42.Platform.Storyteller.Accessing.Model.Permission;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The settings of a project on its access point (spec 424-266 section 3): the defaults, writes and their versions, the
// conditions, the rules, the audit event, and that the other writers of the access point and the settings keep each
// other's members.
public class ProjectSettingsServiceTests(Startup startup)
    : BaseTestsClass(startup)
{
    // one organization for the whole class, every test works in its own project
    private const string Organization = "prs-tests";
    private const string OwnerId = "prs-tests-owner";

    private static readonly KeyValueCaller Admin = KeyValueCaller.WithoutSecrets(AuditActor.Account("prs-admin", "Ada")) with { CorrelationId = "invocation-1" };

    private IProjectSettingsService Settings => Context.Services.GetRequiredService<IProjectSettingsService>();

    private IAccessService Access => Context.Services.GetRequiredService<IAccessService>();

    private IMachineAuthenticationPolicyStore MachinePolicies => Context.Services.GetRequiredService<IMachineAuthenticationPolicyStore>();

    private IKeyValuePolicyStore KeyValuePolicies => Context.Services.GetRequiredService<IKeyValuePolicyStore>();

    private IKeyValueAuditLog Audit => Context.Services.GetRequiredService<IKeyValueAuditLog>();

    private CosmosDbOptions CosmosOptions => Context.Services.GetRequiredService<IOptions<CosmosDbOptions>>().Value;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Get_NewProject_AnswersTheDefaultsWithoutStoringThem()
    {
        var project = await CreateProjectAsync();

        var settings = await Settings.GetAsync(Organization, project, Cancellation);

        settings.Types.Should().BeEmpty();
        settings.DefaultTimeZone.Should().BeNull();
        settings.DateFormat.Should().BeNull();
        settings.Version.Should().Be(0);
        settings.Modified.Should().BeNull();
        (await ReadDocumentAsync(project)).TryGetProperty("Settings", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Set_StoresTheNormalizedSettingsAtTheNextVersion()
    {
        var project = await CreateProjectAsync();
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);

        var input = new ProjectSettings
        {
            Types = new Dictionary<string, ProjectTypeAppearance>
            {
                ["SBT"] = new() { Label = " Company ", Plural = "Companies", Color = "PINK", Icon = "I-LUCIDE-BUILDING-2" },
                ["rst"] = new() { Label = "Solution" },
                ["cnt"] = new() { Label = "  " },
            },
            DefaultTimeZone = "america/phoenix",
            DateFormat = "D MMM YYYY",
            Version = 42,
            Modified = AuditStamp.Of(AuditActor.System, DateTimeOffset.UnixEpoch),
        };

        var written = await Settings.SetAsync(Organization, project, input, ProjectSettingsCondition.None, Admin, Cancellation);

        written.Changed.Should().BeTrue();
        var settings = written.Settings;
        settings.Types.Keys.Should().Equal("rst", "sbt");
        settings.Types["sbt"].Should().Be(new ProjectTypeAppearance { Label = "Company", Plural = "Companies", Color = "pink", Icon = "i-lucide-building-2" });
        settings.Types["rst"].Should().Be(new ProjectTypeAppearance { Label = "Solution" });
        settings.DefaultTimeZone.Should().Be("America/Phoenix");
        settings.DateFormat.Should().Be("d mmm yyyy");
        settings.Version.Should().Be(1);
        settings.Modified!.By.Should().Be(Admin.Actor);
        settings.Modified.At.Should().BeAfter(before);

        var read = await Settings.GetAsync(Organization, project, Cancellation);
        ProjectSettingsRules.SameValues(read, settings).Should().BeTrue();
        read.Version.Should().Be(1);
        read.Modified.Should().Be(settings.Modified);
        read.Types.Keys.Should().Equal("rst", "sbt");

        var stored = (await ReadDocumentAsync(project)).GetProperty("Settings");
        stored.GetProperty("Types").GetProperty("sbt").GetProperty("Color").GetString().Should().Be("pink");
        stored.GetProperty("Types").GetProperty("rst").TryGetProperty("Color", out _).Should().BeFalse();
        stored.GetProperty("Version").GetInt64().Should().Be(1);
    }

    // RFC 7396 over the stored settings: a member left out stays, null removes, a type merges member by member, and a
    // reset to the defaults is a new version, never a missing Settings.
    [Fact]
    public async Task Patch_MergesIntoTheStoredSettingsAndNullRemoves()
    {
        var project = await CreateProjectAsync();
        await Settings.SetAsync(Organization, project, Company(), ProjectSettingsCondition.None, Admin, Cancellation);

        var patched = await Settings.PatchAsync(
            Organization,
            project,
            Patch("""{ "types": { "SBT": { "color": null, "icon": "i-lucide-building-2" }, "rst": { "Label": "Solution" } }, "DateFormat": "dd.mm.yyyy" }"""),
            ProjectSettingsCondition.None,
            Admin,
            Cancellation);

        patched.Changed.Should().BeTrue();
        patched.Settings.Types["sbt"].Should().Be(new ProjectTypeAppearance { Label = "Company", Icon = "i-lucide-building-2" });
        patched.Settings.Types["rst"].Should().Be(new ProjectTypeAppearance { Label = "Solution" });
        patched.Settings.DefaultTimeZone.Should().Be("Europe/Prague");
        patched.Settings.DateFormat.Should().Be("dd.mm.yyyy");
        patched.Settings.Version.Should().Be(2);

        var reset = await Settings.PatchAsync(Organization, project, Patch("""{ "Types": null, "DefaultTimeZone": null, "DateFormat": null }"""), ProjectSettingsCondition.None, Admin, Cancellation);

        reset.Settings.Types.Should().BeEmpty();
        reset.Settings.DefaultTimeZone.Should().BeNull();
        reset.Settings.DateFormat.Should().BeNull();
        reset.Settings.Version.Should().Be(3);
        var stored = (await ReadDocumentAsync(project)).GetProperty("Settings");
        stored.GetProperty("Version").GetInt64().Should().Be(3);
        stored.GetProperty("Types").EnumerateObject().Should().BeEmpty();
    }

    // A write whose result equals the stored settings writes nothing: no version, no event, the same document.
    [Fact]
    public async Task Writes_ThatChangeNothing_KeepTheVersionAndWriteNoEvent()
    {
        var project = await CreateProjectAsync();
        await Settings.SetAsync(Organization, project, Company(), ProjectSettingsCondition.None, Admin, Cancellation);
        var eTag = await ReadETagAsync(project);

        var set = await Settings.SetAsync(Organization, project, Company() with { DefaultTimeZone = "EUROPE/PRAGUE" }, ProjectSettingsCondition.None, Admin, Cancellation);
        var patched = await Settings.PatchAsync(Organization, project, Patch("""{ "Types": { "sbt": { "Label": "Company" }, "unt": null } }"""), ProjectSettingsCondition.None, Admin, Cancellation);

        set.Changed.Should().BeFalse();
        patched.Changed.Should().BeFalse();
        set.Settings.Version.Should().Be(1);
        patched.Settings.Version.Should().Be(1);
        (await ReadETagAsync(project)).Should().Be(eTag);
        (await AuditEventsAsync(project)).Should().ContainSingle();
    }

    // Of a new project, nothing written is no change either.
    [Fact]
    public async Task Set_OfTheDefaultsOnANewProject_WritesNothing()
    {
        var project = await CreateProjectAsync();

        var written = await Settings.SetAsync(Organization, project, new ProjectSettings(), ProjectSettingsCondition.Version(0), Admin, Cancellation);

        written.Changed.Should().BeFalse();
        written.Settings.Version.Should().Be(0);
        (await ReadDocumentAsync(project)).TryGetProperty("Settings", out _).Should().BeFalse();
        (await AuditEventsAsync(project)).Should().BeEmpty();
    }

    [Fact]
    public async Task Writes_WhoseConditionHolds_AreWritten()
    {
        var project = await CreateProjectAsync();

        (await Settings.SetAsync(Organization, project, Company(), ProjectSettingsCondition.Version(0), Admin, Cancellation)).Settings.Version.Should().Be(1);
        (await Settings.PatchAsync(Organization, project, Patch("""{ "DateFormat": "mm/dd/yyyy" }"""), ProjectSettingsCondition.Of([0, 1]), Admin, Cancellation)).Settings.Version.Should().Be(2);
        (await Settings.SetAsync(Organization, project, new ProjectSettings(), ProjectSettingsCondition.Anything, Admin, Cancellation)).Settings.Version.Should().Be(3);
    }

    // A condition is about the settings the caller read: never retried, 412 with the current version, nothing written.
    [Fact]
    public async Task Writes_WhoseConditionFails_ThrowWithTheCurrentVersion()
    {
        var project = await CreateProjectAsync();
        await Settings.SetAsync(Organization, project, Company(), ProjectSettingsCondition.None, Admin, Cancellation);

        var set = () => Settings.SetAsync(Organization, project, new ProjectSettings(), ProjectSettingsCondition.Version(0), Admin, Cancellation);
        var patch = () => Settings.PatchAsync(Organization, project, Patch("""{ "DateFormat": "mm/dd/yyyy" }"""), new ProjectSettingsCondition([], false), Admin, Cancellation);

        foreach (var write in new[] { set, patch })
        {
            var failed = (await write.Should().ThrowAsync<ProjectSettingsPreconditionFailedException>()).Which;
            failed.CurrentVersion.Should().Be(1);
            failed.ErrorCode.Should().Be(ErrorCodes.SettingsPreconditionFailed);
            failed.Message.Should().Be("The project settings changed meanwhile; they are at version 1.");
        }

        (await Settings.GetAsync(Organization, project, Cancellation)).Version.Should().Be(1);
    }

    [Fact]
    public async Task Writes_OfInvalidSettings_ThrowEveryProblemAndWriteNothing()
    {
        var project = await CreateProjectAsync();
        var invalid = new ProjectSettings
        {
            Types = new Dictionary<string, ProjectTypeAppearance>
            {
                ["xyz"] = new() { Label = "Nothing" },
                ["sbt"] = new() { Color = "beige", Plural = "Companies" },
            },
            DefaultTimeZone = "Central Europe Standard Time",
            DateFormat = "yy",
        };

        var set = () => Settings.SetAsync(Organization, project, invalid, ProjectSettingsCondition.None, Admin, Cancellation);
        var exception = (await set.Should().ThrowAsync<ProjectSettingsInvalidException>()).Which;

        exception.ErrorCode.Should().Be(ErrorCodes.InvalidSettings);
        exception.Problems.Select(problem => (problem.Path, problem.Rule)).Should().BeEquivalentTo(new[]
        {
            ("$.Types.xyz", ProjectSettingsRules.TypeCode),
            ("$.Types.sbt.Plural", ProjectSettingsRules.PluralWithoutLabel),
            ("$.Types.sbt.Color", ProjectSettingsRules.Color),
            ("$.DefaultTimeZone", ProjectSettingsRules.TimeZone),
            ("$.DateFormat", ProjectSettingsRules.DateFormat),
        });

        var patch = () => Settings.PatchAsync(Organization, project, Patch("""{ "Types": { "sbt": { "Label": 7 } }, "Colour": "pink" }"""), ProjectSettingsCondition.None, Admin, Cancellation);
        (await patch.Should().ThrowAsync<ProjectSettingsInvalidException>()).Which.Problems.Select(problem => problem.Rule)
            .Should().BeEquivalentTo([ProjectSettingsRules.WrongType, ProjectSettingsRules.UnknownMember]);

        (await ReadDocumentAsync(project)).TryGetProperty("Settings", out _).Should().BeFalse();
        (await AuditEventsAsync(project)).Should().BeEmpty();
    }

    // Names of a patch that differ only in case are refused, so neither the later one wins nor a null one removes what
    // the patch has just set; a name that is no member is refused also with null.
    [Fact]
    public async Task Patch_WithNamesThatCollideOrAreNoMembers_ThrowsAndWritesNothing()
    {
        var project = await CreateProjectAsync();
        var written = (await Settings.SetAsync(Organization, project, Company(), ProjectSettingsCondition.None, Admin, Cancellation)).Settings;

        var patch = () => Settings.PatchAsync(
            Organization,
            project,
            Patch("""{ "Types": { "rst": { "Color": "rose" }, "RST": null, "sbt": { "Color": "blue", "color": "rose" } }, "DateFormat": "dd.mm.yyyy", "dateFormat": "mm/dd/yyyy", "Theme": null }"""),
            ProjectSettingsCondition.None,
            Admin,
            Cancellation);
        var exception = (await patch.Should().ThrowAsync<ProjectSettingsInvalidException>()).Which;

        exception.Problems.Select(problem => (problem.Path, problem.Rule)).Should().BeEquivalentTo(new[]
        {
            ("$.Theme", ProjectSettingsRules.UnknownMember),
            ("$.DateFormat", ProjectSettingsRules.CaseCollision),
            ("$.Types.rst", ProjectSettingsRules.TypeCode),
            ("$.Types.sbt.Color", ProjectSettingsRules.CaseCollision),
        });

        var read = await Settings.GetAsync(Organization, project, Cancellation);
        read.Version.Should().Be(written.Version);
        ProjectSettingsRules.SameValues(read, written).Should().BeTrue();
        (await AuditEventsAsync(project)).Should().ContainSingle();
    }

    [Fact]
    public async Task UnknownProject_ThrowsNotFound()
    {
        var project = NewProjectName();

        await FluentActions.Awaiting(() => Settings.GetAsync(Organization, project, Cancellation)).Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => Settings.SetAsync(Organization, project, Company(), ProjectSettingsCondition.None, Admin, Cancellation)).Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => Settings.PatchAsync(Organization, project, Patch("{}"), ProjectSettingsCondition.None, Admin, Cancellation)).Should().ThrowAsync<NotFoundException>();
    }

    // The replace changes only Settings of the document it read: the members, the policies and the description stay.
    [Fact]
    public async Task Write_KeepsTheOtherMembersOfTheAccessPoint()
    {
        var project = await CreateProjectAsync("Described.");
        var pointKey = $"{Organization}.{project}";
        var memberId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(memberId, pointKey, AccountRole.Reader));
        await MachinePolicies.SetAsync(Organization, project, new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.Certificate });
        await KeyValuePolicies.SetAsync(Organization, project, new KeyValuePolicy { RequireMtlsForSecretDelivery = false, Version = 1 }, Cancellation);

        await Settings.SetAsync(Organization, project, Company(), ProjectSettingsCondition.None, Admin, Cancellation);

        var stored = await ReadPointEntityAsync(project);
        stored.Description.Should().Be("Described.");
        stored.AccessMap.Should().Contain(OwnerId, AccountRole.Owner).And.Contain(memberId, AccountRole.Reader);
        stored.MachineAuthentication!.CredentialKind.Should().Be(MachineCredentialKind.Certificate);
        stored.KeyValues!.Version.Should().Be(1UL);
        stored.Settings!.Version.Should().Be(1);
    }

    // Every other writer of the access point replaces the whole document, so each must keep the settings: a membership,
    // the description, the key/value policy, the machine policy and DbCreator's base seed (the Newtonsoft read).
    [Fact]
    public async Task LaterWrites_KeepTheSettings()
    {
        var project = await CreateProjectAsync();
        var pointKey = $"{Organization}.{project}";
        var memberId = await CreateBareAccountAsync();
        var expected = (await Settings.SetAsync(Organization, project, Company(), ProjectSettingsCondition.None, Admin, Cancellation)).Settings;

        await Access.GrantPermissionAsync(Permission(memberId, pointKey, AccountRole.Contributor));
        await ShouldKeepAsync();

        await Access.UpdateAccessPointAsync(pointKey, new AccessPointUpdate { Description = "Described." });
        await ShouldKeepAsync();

        await KeyValuePolicies.SetAsync(Organization, project, new KeyValuePolicy { RequireMtlsForSecretDelivery = false, Version = 1 }, Cancellation);
        await ShouldKeepAsync();

        await MachinePolicies.SetAsync(Organization, project, new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.ApiKey });
        await ShouldKeepAsync();

        var core = Context.Services.GetRequiredService<IContainerRepositoryProvider>().GetCore().Container;
        await Data.SeedAccountAccessAsync(core, memberId, $"{memberId}@example.com", memberId, new Dictionary<string, AccountRole> { [pointKey] = AccountRole.Administrator });
        await ShouldKeepAsync();

        async Task ShouldKeepAsync()
        {
            var settings = await Settings.GetAsync(Organization, project, Cancellation);
            ProjectSettingsRules.SameValues(settings, expected).Should().BeTrue();
            settings.Version.Should().Be(expected.Version);
            settings.Modified.Should().Be(expected.Modified);
        }
    }

    // A concurrent write between the read and the replace only moves the ETag: the retry reads it, and both land.
    [Fact]
    public async Task Write_GrantBeforeReplace_RetriesAndKeepsBoth()
    {
        var project = await CreateProjectAsync();
        var pointKey = $"{Organization}.{project}";
        var memberId = await CreateBareAccountAsync();
        var interceptor = new ReplaceInterceptor($"apt.{pointKey}", async replace =>
        {
            if (replace == 1)
            {
                await Access.GrantPermissionAsync(Permission(memberId, pointKey, AccountRole.Reader));
            }
        });
        using var client = interceptor.CreateClient(CosmosOptions);

        var written = await SettingsOn(client).PatchAsync(Organization, project, Patch("""{ "DateFormat": "dd/mm/yyyy" }"""), ProjectSettingsCondition.Version(0), Admin, Cancellation);

        interceptor.Replaces.Should().Be(2);
        written.Settings.Version.Should().Be(1);
        (await Access.GetAccessPointAsync(pointKey))!.AccessMap.Should().Contain(memberId, AccountRole.Reader);
        (await Settings.GetAsync(Organization, project, Cancellation)).DateFormat.Should().Be("dd/mm/yyyy");
        (await AuditEventsAsync(project)).Should().ContainSingle();
    }

    [Fact]
    public async Task Write_ChangedBeforeEveryReplace_ThrowsConflict()
    {
        var project = await CreateProjectAsync();
        var pointKey = $"{Organization}.{project}";
        var memberIds = new[] { await CreateBareAccountAsync(), await CreateBareAccountAsync() };
        var interceptor = new ReplaceInterceptor($"apt.{pointKey}", replace =>
            Access.GrantPermissionAsync(Permission(memberIds[replace - 1], pointKey, AccountRole.Reader)));
        using var client = interceptor.CreateClient(CosmosOptions);

        var write = () => SettingsOn(client).SetAsync(Organization, project, Company(), ProjectSettingsCondition.None, Admin, Cancellation);

        (await write.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.Conflict);
        interceptor.Replaces.Should().Be(2);
        (await Access.GetAccessPointAsync(pointKey))!.AccessMap.Should().ContainKeys(memberIds);
        (await Settings.GetAsync(Organization, project, Cancellation)).Version.Should().Be(0);
        (await AuditEventsAsync(project)).Should().BeEmpty();
    }

    // In the project's audit trail: the caller, no view or key, the versions and the names of the changed parts.
    [Fact]
    public async Task Changes_AreAudited()
    {
        var project = await CreateProjectAsync();

        await Settings.SetAsync(Organization, project, Company(), ProjectSettingsCondition.None, Admin, Cancellation);
        await Settings.PatchAsync(Organization, project, Patch("""{ "Types": { "sbt": { "Color": "teal" }, "exe": { "Label": "Run" } }, "DateFormat": "dd.mm.yyyy" }"""), ProjectSettingsCondition.None, KeyValueCaller.System(), Cancellation);

        var events = await AuditEventsAsync(project);

        events.Should().HaveCount(2);
        var system = events.Single(entry => entry.Detail!.StartsWith("v1", StringComparison.Ordinal));
        system.Detail.Should().Be("v1→v2: Types.sbt, Types.exe, DateFormat");
        system.Actor.Should().Be(AuditActor.System);
        system.Channel.Should().Be(KeyValueChannel.System);
        var user = events.Single(entry => entry.Detail!.StartsWith("v0", StringComparison.Ordinal));
        user.Detail.Should().Be("v0→v1: Types.sbt, DefaultTimeZone");
        user.Actor.Should().Be(Admin.Actor);
        user.Channel.Should().Be(KeyValueChannel.User);
        user.CorrelationId.Should().Be("invocation-1");
        user.View.Should().BeNull();
        user.Key.Should().BeNull();
    }

    // Best effort: the change is stored when its event cannot be appended.
    [Fact]
    public async Task Write_WhoseEventFails_IsStored()
    {
        var project = await CreateProjectAsync();
        var service = new CosmosProjectSettingsService(
            Context.Services.GetRequiredService<IContainerRepositoryProvider>(),
            Context.Services.GetRequiredService<IOptions<JsonSerializerOptions>>(),
            new FailingAuditLog());

        var written = await service.SetAsync(Organization, project, Company(), ProjectSettingsCondition.None, Admin, Cancellation);

        written.Changed.Should().BeTrue();
        (await Settings.GetAsync(Organization, project, Cancellation)).Version.Should().Be(1);
    }

    // Every project of the caller with its settings, the defaults when none were set; an Administrator's read of the
    // access point has them too, and an organization none.
    [Fact]
    public async Task ProjectsAndAccessPoints_CarryTheSettings()
    {
        var described = await CreateProjectAsync();
        var plain = await CreateProjectAsync();
        var memberId = await CreateBareAccountAsync();
        await Access.GrantPermissionAsync(Permission(memberId, $"{Organization}.{described}", AccountRole.Reader));
        await Access.GrantPermissionAsync(Permission(memberId, $"{Organization}.{plain}", AccountRole.Reader));
        var expected = (await Settings.SetAsync(Organization, described, Company(), ProjectSettingsCondition.None, Admin, Cancellation)).Settings;

        var projects = (await Access.GetProjectsAsync(memberId)).ToDictionary(summary => summary.Key);

        var withSettings = projects[$"{Organization}.{described}"].Settings!;
        ProjectSettingsRules.SameValues(withSettings, expected).Should().BeTrue();
        withSettings.Version.Should().Be(1);
        withSettings.Modified.Should().Be(expected.Modified);
        var defaults = projects[$"{Organization}.{plain}"].Settings!;
        defaults.Version.Should().Be(0);
        defaults.Types.Should().BeEmpty();

        var point = (await Access.GetAccessPointAsync($"{Organization}.{described}"))!;
        ProjectSettingsRules.SameValues(point.Settings, expected).Should().BeTrue();
        point.Settings!.Version.Should().Be(1);
        (await Access.GetAccessPointAsync($"{Organization}.{plain}"))!.Settings!.Version.Should().Be(0);
        (await Access.GetAccessPointAsync(Organization))!.Settings.Should().BeNull();
    }

    private static ProjectSettings Company() => new()
    {
        Types = new Dictionary<string, ProjectTypeAppearance> { ["sbt"] = new() { Label = "Company", Color = "pink" } },
        DefaultTimeZone = "Europe/Prague",
    };

    private static JsonObject Patch(string json) => JsonNode.Parse(json)!.AsObject();

    private static Permission Permission(string accountId, string pointKey, AccountRole role)
    {
        return new Permission
        {
            CreatedById = OwnerId,
            AccountId = accountId,
            AccessPointKey = pointKey,
            Role = role,
        };
    }

    private static string NewProjectName() => $"p-{Guid.NewGuid():N}"[..14];

    // The service on a client of its own, for example one with a ReplaceInterceptor; the audit log of the fixture.
    private CosmosProjectSettingsService SettingsOn(CosmosClient client)
    {
        return new CosmosProjectSettingsService(
            new ContainerRepositoryProvider(new FixedClientProvider(client)),
            Context.Services.GetRequiredService<IOptions<JsonSerializerOptions>>(),
            Audit);
    }

    // Newest first.
    private async Task<IReadOnlyList<KeyValueAuditEvent>> AuditEventsAsync(string project)
    {
        var response = await Audit.QueryAsync(Organization, project, new KeyValueAuditQuery { Operation = KeyValueAuditOperation.ProjectSettingsChange }, Cancellation);
        return response.Items;
    }

    private async Task EnsureOwnerAsync()
    {
        if (await Access.GetAccountAsync(OwnerId) is not null)
        {
            return;
        }

        await Access.CreateAccountAsync(new AccountCreate
        {
            IdentityId = OwnerId,
            UserName = "prs-owner@example.com",
            Name = "Owner",
            Organization = Organization,
            Project = "base",
        });
    }

    private async Task<string> CreateProjectAsync(string description = null)
    {
        await EnsureOwnerAsync();
        var project = NewProjectName();
        await Access.CreateAccessPointAsync(new AccessPointCreate
        {
            Organization = Organization,
            Project = project,
            ProjectDescription = description,
            OwnerId = OwnerId,
        });
        return project;
    }

    private async Task<string> CreateBareAccountAsync()
    {
        var id = $"acc-{Guid.NewGuid():N}";
        var repository = Context.Services.GetRequiredService<IContainerRepositoryProvider>().GetCore();
        await repository.Container.CreateItemAsync(
            new AccountEntity { Id = id, UserName = $"{id}@example.com", Name = id, AccessMap = new() },
            new PartitionKey("access"),
            cancellationToken: Cancellation);
        return id;
    }

    // The stored document as written: the Cosmos client leaves out null values.
    private async Task<JsonElement> ReadDocumentAsync(string project)
    {
        var repository = Context.Services.GetRequiredService<IContainerRepositoryProvider>().GetCore();
        using var response = await repository.Container.ReadItemStreamAsync($"apt.{Organization}.{project}", new PartitionKey("access"), cancellationToken: Cancellation);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(response.Content, cancellationToken: Cancellation);
        return document.RootElement.Clone();
    }

    private async Task<string> ReadETagAsync(string project)
    {
        var repository = Context.Services.GetRequiredService<IContainerRepositoryProvider>().GetCore();
        using var response = await repository.Container.ReadItemStreamAsync($"apt.{Organization}.{project}", new PartitionKey("access"), cancellationToken: Cancellation);
        response.EnsureSuccessStatusCode();
        return response.Headers.ETag;
    }

    private async Task<AccessPointEntity> ReadPointEntityAsync(string project)
    {
        var repository = Context.Services.GetRequiredService<IContainerRepositoryProvider>().GetCore();
        var options = Context.Services.GetRequiredService<IOptions<JsonSerializerOptions>>().Value;
        return await repository.Container.TryReadItemAsync(
            $"apt.{Organization}.{project}",
            new PartitionKey("access"),
            stream => stream.DeserializeSystemTextJson<AccessPointEntity>(options),
            cancellationToken: Cancellation);
    }

    private sealed class FixedClientProvider(CosmosClient client) : ICosmosClientProvider
    {
        public CosmosClient Client => client;
    }

    private sealed class FailingAuditLog : IKeyValueAuditLog
    {
        public Task WriteAsync(string organization, string project, KeyValueAuditEvent entry, CancellationToken cancellationToken = default)
            => throw new CosmosException("The audit trail is not available.", System.Net.HttpStatusCode.ServiceUnavailable, 0, "activity", 0);

        public void RecordBindingRead(string organization, string project, string view, string key, KeyValueProtection protection, KeyValueCaller caller)
        {
        }

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<KeyValueAuditResponse> QueryAsync(string organization, string project, KeyValueAuditQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
