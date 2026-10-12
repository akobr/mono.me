using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Api;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.Policy;

using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// Who may receive key/value secrets: the role of a user, the scope of a machine, and the mTLS rule of the project.
public class SecretRightsTests
{
    private const string Org = "acme";
    private const string Project = "billing";

    [Theory]
    [InlineData(AccountRole.Reader, false)]
    [InlineData(AccountRole.Contributor, false)]
    [InlineData(AccountRole.ContributorWithSecrets, true)]
    [InlineData(AccountRole.Administrator, true)]
    public async Task User_NeedsContributorWithSecrets(AccountRole role, bool expected)
    {
        if (HttpRequestDataExtensions.IsDevelopmentAuthentication && !expected)
        {
            // a Debug build grants every scope, so the role is never asked
            return;
        }

        using var services = new ServiceCollection().BuildServiceProvider();
        var (context, request) = FunctionTestDoubles.CreateRequest(services);
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim> { new("sub", "user-1"), new("scp", "User.Impersonation") };

        var rights = await request.GetSecretRightsAsync(new FakeAccess(role), new FakePolicies(requireMtls: true), Org, Project);

        rights.CanReadSecrets.ShouldBe(expected);
        rights.Caller.CanWriteSecrets.ShouldBe(expected);
        rights.Caller.Channel.ShouldBe(KeyValueChannel.User);
        rights.Caller.Author.ShouldBe("account: user-1");
        rights.IsWithheldByMtls.ShouldBeFalse();
        rights.AsAmbient().ShouldBeSameAs(rights);
        KeyValueCallerContext.Current.ShouldBe(rights.Caller);
    }

    [Fact]
    public async Task User_WithTheSecretsScope_DoesNotNeedTheRole()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var (context, request) = FunctionTestDoubles.CreateRequest(services);
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim> { new("sub", "user-1"), new("scp", "User.Impersonation Configuration.Secrets") };

        var rights = await request.GetSecretRightsAsync(new FakeAccess(AccountRole.Reader), new FakePolicies(requireMtls: true), Org, Project);

        rights.CanReadSecrets.ShouldBeTrue();
    }

    [Theory]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, true)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, true, false)]
    public async Task Machine_NeedsTheScopeAndMtlsWhenTheProjectSaysSo(bool requireMtls, bool presentsCertificate, bool expectedAllowed, bool expectedWithheld)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var (context, request) = FunctionTestDoubles.CreateRequest(services);
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim> { new("sub", "machine-1"), new("roles", "Configuration.Read"), new("roles", "Configuration.Secrets") };
        context.Items[FunctionContextItemKeys.MachineIdentity] = "machine-1";

        if (presentsCertificate)
        {
            context.Items[FunctionContextItemKeys.PresentingCertificateThumbprint] = "ABCD";
        }

        var rights = await request.GetSecretRightsAsync(new FakeAccess(AccountRole.None), new FakePolicies(requireMtls), Org, Project);

        rights.CanReadSecrets.ShouldBe(expectedAllowed);
        rights.IsWithheldByMtls.ShouldBe(expectedWithheld);
        rights.Caller.Channel.ShouldBe(presentsCertificate ? KeyValueChannel.MachineMtls : KeyValueChannel.MachineToken);
        rights.Caller.Author.ShouldBe("application: machine-1");
    }

    [Fact]
    public async Task Machine_WithoutTheScope_GetsNothingAndNothingIsWithheld()
    {
        if (HttpRequestDataExtensions.IsDevelopmentAuthentication)
        {
            return;
        }

        using var services = new ServiceCollection().BuildServiceProvider();
        var (context, request) = FunctionTestDoubles.CreateRequest(services);
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim> { new("sub", "machine-1"), new("roles", "Configuration.Read") };
        context.Items[FunctionContextItemKeys.MachineIdentity] = "machine-1";

        var rights = await request.GetSecretRightsAsync(new FakeAccess(AccountRole.None), new FakePolicies(requireMtls: true), Org, Project);

        rights.CanReadSecrets.ShouldBeFalse();
        rights.IsWithheldByMtls.ShouldBeFalse("there was nothing to withhold");
    }

    private sealed class FakePolicies(bool requireMtls) : IKeyValuePolicyStore
    {
        public Task<KeyValuePolicy> GetAsync(string organization, string project, CancellationToken cancellationToken = default)
            => Task.FromResult(new KeyValuePolicy { RequireMtlsForSecretDelivery = requireMtls });

        public Task<KeyValuePolicy> SetAsync(string organization, string project, KeyValuePolicy policy, CancellationToken cancellationToken = default)
            => Task.FromResult(policy);

        public Task<IReadOnlyList<(string Organization, string Project, KeyValuePolicy Policy)>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<(string, string, KeyValuePolicy)>>([]);
    }

    private sealed class FakeAccess(AccountRole role) : IAccessService
    {
        public Task<AccountRole> GetAccountRoleAsync(string accountId, string accessPointKey) => Task.FromResult(role);

        public Task<Account?> GetAccountAsync(string id) => throw new NotSupportedException();

        public Task<Account> CreateAccountAsync(AccountCreate model) => throw new NotSupportedException();

        public Task<IEnumerable<AccessPoint>> GetAccessPointsAsync(string accountId) => throw new NotSupportedException();

        public Task<AccessPoint?> GetAccessPointAsync(string key) => throw new NotSupportedException();

        public Task<AccessPoint> CreateAccessPointAsync(AccessPointCreate model) => throw new NotSupportedException();

        public Task<bool> GrantPermissionAsync(Permission model) => throw new NotSupportedException();

        public Task<bool> RevokePermissionAsync(Permission model) => throw new NotSupportedException();

        public Task<IReadOnlyList<AccessPointMember>> GetMembersAsync(string accessPointKey, string actorId) => throw new NotSupportedException();

        public Task<AccessPointMember> SetMemberRoleAsync(string accessPointKey, string accountId, AccountRole newRole, string actorId) => throw new NotSupportedException();

        public Task RemoveMemberAsync(string accessPointKey, string accountId, string actorId) => throw new NotSupportedException();

        public Task<AccountRole> JoinAccessPointAsync(string accessPointKey, string accountId, AccountRole newRole) => throw new NotSupportedException();

        public Task<IEnumerable<MachineAccess>> GetMachineAccessesAsync(string organization, string project) => throw new NotSupportedException();

        public Task<MachineAccess?> GetMachineAccessAsync(string organization, string project, string id) => throw new NotSupportedException();

        public Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model) => throw new NotSupportedException();

        public Task<MachineAccess> ResetMachineAccessAsync(string organization, string project, string appId) => throw new NotSupportedException();

        public Task<bool> DeleteMachineAccessAsync(string organization, string project, string appId) => throw new NotSupportedException();

        public Task<bool> VerifyAccessForMachineAsync(string organization, string project, string appId) => throw new NotSupportedException();
    }
}
