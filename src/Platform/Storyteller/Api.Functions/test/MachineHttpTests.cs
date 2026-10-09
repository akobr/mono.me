using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class MachineHttpTests
{
    private const string KeycloakId = "42.sform.org1.proj1.0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task GetMachine_StoredRecord_ReturnsIt()
    {
        var stored = Machine();
        var access = new RecordingAccess(stored);
        var http = CreateHttp(access);

        var result = await http.GetMachine(ContributorRequest(), "org1", "proj1", KeycloakId);

        var ok = result.ShouldBeOfType<OkObjectResult>();
        ok.Value.ShouldBeSameAs(stored);
        access.Calls.ShouldBe([$"get:org1/proj1/{KeycloakId}"]);
    }

    [Fact]
    public async Task KeycloakId_GetResetAndDelete_ReachTheAccessService()
    {
        var access = new RecordingAccess(Machine());
        var http = CreateHttp(access);

        (await http.GetMachine(ContributorRequest(), "org1", "proj1", KeycloakId)).ShouldBeOfType<OkObjectResult>();
        var reset = await http.PutMachine(ContributorRequest(), "org1", "proj1", KeycloakId);
        (await http.DeleteMachine(ContributorRequest(), "org1", "proj1", KeycloakId)).ShouldBeOfType<OkResult>();

        reset.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<MachineAccess>().AccessKey.ShouldBe("new-secret");
        access.Calls.ShouldBe(
            [
                $"get:org1/proj1/{KeycloakId}",
                $"reset:org1/proj1/{KeycloakId}",
                $"delete:org1/proj1/{KeycloakId}",
            ]);
    }

    private static AccessHttp CreateHttp(RecordingAccess access)
    {
        return new AccessHttp(access, NullLogger<AccessHttp>.Instance);
    }

    private static TestHttpRequestData ContributorRequest()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var (context, request) = FunctionTestDoubles.CreateRequest(services);
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim>
        {
            new("sub", "user-1"),
            new("scp", Scopes.User.Impersonation),
        };
        return request;
    }

    private static MachineAccess Machine()
    {
        return new MachineAccess
        {
            Id = KeycloakId,
            ObjectId = "6f1c2d3e-4a5b-4c6d-8e7f-901a2b3c4d5e",
            AccessKey = "kc-***",
            Scope = MachineAccessScope.DefaultRead,
            CredentialKind = MachineCredentialKind.ClientCredentials,
        };
    }

    private sealed class RecordingAccess(MachineAccess machine) : IAccessService
    {
        public List<string> Calls { get; } = [];

        public Task<AccountRole> GetAccountRoleAsync(string accountId, string accessPointKey)
        {
            Calls.Add($"role:{accessPointKey}");
            return Task.FromResult(AccountRole.Contributor);
        }

        public Task<MachineAccess?> GetMachineAccessAsync(string organization, string project, string id)
        {
            Calls.Add($"get:{organization}/{project}/{id}");
            return Task.FromResult<MachineAccess?>(machine);
        }

        public Task<MachineAccess> ResetMachineAccessAsync(string organization, string project, string appId)
        {
            Calls.Add($"reset:{organization}/{project}/{appId}");
            return Task.FromResult(machine with { AccessKey = "new-secret" });
        }

        public Task<bool> DeleteMachineAccessAsync(string organization, string project, string appId)
        {
            Calls.Add($"delete:{organization}/{project}/{appId}");
            return Task.FromResult(true);
        }

        public Task<Account?> GetAccountAsync(string id) => throw new NotImplementedException();

        public Task<Account> CreateAccountAsync(AccountCreate model) => throw new NotImplementedException();

        public Task<IEnumerable<AccessPoint>> GetAccessPointsAsync(string accountId) => throw new NotImplementedException();

        public Task<AccessPoint?> GetAccessPointAsync(string key) => throw new NotImplementedException();

        public Task<AccessPoint> CreateAccessPointAsync(AccessPointCreate model) => throw new NotImplementedException();

        public Task<bool> GrantPermissionAsync(Permission model) => throw new NotImplementedException();

        public Task<bool> RevokePermissionAsync(Permission model) => throw new NotImplementedException();

        public Task<IEnumerable<MachineAccess>> GetMachineAccessesAsync(string organization, string project) => throw new NotImplementedException();

        public Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model) => throw new NotImplementedException();

        public Task<bool> VerifyAccessForMachineAsync(string organization, string project, string appId) => throw new NotImplementedException();

        public Task<IReadOnlyList<AccessPointMember>> GetMembersAsync(string accessPointKey, string actorId) => throw new NotImplementedException();

        public Task<AccessPointMember> SetMemberRoleAsync(string accessPointKey, string accountId, AccountRole role, string actorId) => throw new NotImplementedException();

        public Task RemoveMemberAsync(string accessPointKey, string accountId, string actorId) => throw new NotImplementedException();

        public Task<AccountRole> JoinAccessPointAsync(string accessPointKey, string accountId, AccountRole role) => throw new NotImplementedException();
    }
}
