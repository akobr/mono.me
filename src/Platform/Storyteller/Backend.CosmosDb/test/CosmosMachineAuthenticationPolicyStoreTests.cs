using System;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.KeyValues;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The machine authentication policy of a project replaces its access point under the ETag it read (spec 424-266
// section 3.6): a concurrent write of the project, such as its settings, is retried once and never dropped.
public class CosmosMachineAuthenticationPolicyStoreTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Organization = "mpo-tests";
    private const string OwnerId = "mpo-tests-owner";

    private IAccessService Access => Context.Services.GetRequiredService<IAccessService>();

    private IMachineAuthenticationPolicyStore Policies => Context.Services.GetRequiredService<IMachineAuthenticationPolicyStore>();

    private IProjectSettingsService Settings => Context.Services.GetRequiredService<IProjectSettingsService>();

    private CosmosDbOptions CosmosOptions => Context.Services.GetRequiredService<IOptions<CosmosDbOptions>>().Value;

    [Fact]
    public async Task Set_StoresThePolicyAndKeepsTheRestOfTheProject()
    {
        var project = await CreateProjectAsync("Kept.");
        await Settings.SetAsync(Organization, project, DateFormat("dd.mm.yyyy"), ProjectSettingsCondition.None, KeyValueCaller.System());

        await Policies.SetAsync(Organization, project, new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.Certificate }, AccessCaller.System);

        (await Policies.GetAsync(Organization, project))!.CredentialKind.Should().Be(MachineCredentialKind.Certificate);
        var point = (await Access.GetAccessPointAsync($"{Organization}.{project}"))!;
        point.MachineAuthentication!.CredentialKind.Should().Be(MachineCredentialKind.Certificate);
        point.Description.Should().Be("Kept.");
        point.AccessMap.Should().Contain(OwnerId, AccountRole.Owner);
        point.Settings!.DateFormat.Should().Be("dd.mm.yyyy");
        point.Settings.Version.Should().Be(1);
    }

    // The settings change between the policy's read and its replace: the replace fails on the ETag, and the retry keeps
    // the change (the upsert before 424-266 dropped it).
    [Fact]
    public async Task Set_SettingsChangedBeforeTheReplace_RetriesAndKeepsThem()
    {
        var project = await CreateProjectAsync();
        var interceptor = new ReplaceInterceptor($"apt.{Organization}.{project}", async replace =>
        {
            if (replace == 1)
            {
                await Settings.SetAsync(Organization, project, DateFormat("mm/dd/yyyy"), ProjectSettingsCondition.None, KeyValueCaller.System());
            }
        });
        using var client = interceptor.CreateClient(CosmosOptions);

        await PoliciesOn(client).SetAsync(Organization, project, new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.ApiKey }, AccessCaller.System);

        interceptor.Replaces.Should().Be(2);
        var point = (await Access.GetAccessPointAsync($"{Organization}.{project}"))!;
        point.MachineAuthentication!.CredentialKind.Should().Be(MachineCredentialKind.ApiKey);
        point.Settings!.DateFormat.Should().Be("mm/dd/yyyy");
    }

    [Fact]
    public async Task Set_ChangedBeforeEveryReplace_ThrowsConflict()
    {
        var project = await CreateProjectAsync();
        var formats = new[] { "mm/dd/yyyy", "dd/mm/yyyy" };
        var interceptor = new ReplaceInterceptor($"apt.{Organization}.{project}", replace =>
            Settings.SetAsync(Organization, project, DateFormat(formats[replace - 1]), ProjectSettingsCondition.None, KeyValueCaller.System()));
        using var client = interceptor.CreateClient(CosmosOptions);

        var set = () => PoliciesOn(client).SetAsync(Organization, project, new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.ApiKey }, AccessCaller.System);

        (await set.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.Conflict);
        interceptor.Replaces.Should().Be(2);
        var point = (await Access.GetAccessPointAsync($"{Organization}.{project}"))!;
        point.MachineAuthentication.Should().BeNull();
        point.Settings!.DateFormat.Should().Be("dd/mm/yyyy");
        point.Settings.Version.Should().Be(2);
    }

    [Fact]
    public async Task Set_UnknownProject_Throws()
    {
        var set = () => Policies.SetAsync(Organization, NewProjectName(), new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.ApiKey }, AccessCaller.System);

        await set.Should().ThrowAsync<InvalidOperationException>();
    }

    private static ProjectSettings DateFormat(string format) => new() { DateFormat = format };

    private static string NewProjectName() => $"p-{Guid.NewGuid():N}"[..14];

    // The store on a client of its own, with a ReplaceInterceptor; a cache of its own.
    private CosmosMachineAuthenticationPolicyStore PoliciesOn(CosmosClient client)
    {
        return new CosmosMachineAuthenticationPolicyStore(
            new ContainerRepositoryProvider(new FixedClientProvider(client)),
            Context.Services.GetRequiredService<IOptions<JsonSerializerOptions>>(),
            new MemoryCache(new MemoryCacheOptions()));
    }

    private async Task<string> CreateProjectAsync(string description = null)
    {
        if (await Access.GetAccountAsync(OwnerId) is null)
        {
            await Access.CreateAccountAsync(new AccountCreate
            {
                IdentityId = OwnerId,
                UserName = "mpo-owner@example.com",
                Name = "Owner",
                Organization = Organization,
                Project = "base",
            });
        }

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

    private sealed class FixedClientProvider(CosmosClient client) : ICosmosClientProvider
    {
        public CosmosClient Client => client;
    }
}
