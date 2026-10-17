using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api;
using _42.Platform.Storyteller.Api.Security;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class MachineAccessRulesTests
{
    private const string FederatedMachineId = "3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2d";

    [Theory]
    [InlineData("3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2d", true)]
    [InlineData("client_01HXYZ123456789ABCDEFGHIJ", true)]
    [InlineData("42.sform.org1.proj1.0123456789abcdef0123456789abcdef", true)]
    [InlineData("42.sform.org1.proj1.abc", false)]
    [InlineData("42.sform.org1.proj1.extra.0123456789abcdef0123456789abcdef", false)]
    [InlineData("42.sform.org1/../proj1.0123456789abcdef0123456789abcdef", false)]
    [InlineData("client_", false)]
    [InlineData("client_01/../x", false)]
    [InlineData("conn_app_01HXYZ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void MachineIds_AcceptGuidsAndWorkOsClientIds(string? id, bool expected)
    {
        MachineIds.IsValid(id).ShouldBe(expected);
    }

    [Theory]
    [InlineData(MachineAccessScope.AnnotationRead, new[] { Scopes.Annotation.Read })]
    [InlineData(MachineAccessScope.ConfigurationReadWrite, new[] { Scopes.Configuration.Read, Scopes.Configuration.Write })]
    [InlineData(MachineAccessScope.DefaultReadWrite, new[] { Scopes.Default.Read, Scopes.Default.Write, Scopes.Annotation.Read, Scopes.Annotation.Write, Scopes.Configuration.Read, Scopes.Configuration.Write })]
    [InlineData(MachineAccessScope.SecretsRead, new[] { Scopes.Configuration.Read, Scopes.Configuration.Secrets })]
    [InlineData(MachineAccessScope.SecretsReadWrite, new[] { Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Configuration.Secrets })]
    public void MachineScopeRoles_MatchTheApiScopeNames(MachineAccessScope scope, string[] expected)
    {
        MachineScopeClaims.GetRoleClaimsForScope(scope).ShouldBe(expected);
    }

    [Fact]
    public async Task Policy_MachineWithoutBearerCredential_IsNotChecked()
    {
        var store = new FixedPolicyStore(MachineCredentialKind.ApiKey);
        var context = CreateContext(store, credentialKind: null);

        await MachineCredentialPolicy.EnsureAllowedAsync(context, "org1", "proj1");

        store.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task Policy_ClientCredentialsProject_AcceptsTheBearerMachine()
    {
        var context = CreateContext(new FixedPolicyStore(MachineCredentialKind.ClientCredentials), MachineCredentialKind.ClientCredentials);

        await MachineCredentialPolicy.EnsureAllowedAsync(context, "org1", "proj1");
    }

    [Theory]
    [InlineData(MachineCredentialKind.ApiKey)]
    [InlineData(MachineCredentialKind.Certificate)]
    [InlineData(MachineCredentialKind.CertificateAndApiKey)]
    public async Task Policy_OtherProjects_RejectTheBearerMachine(MachineCredentialKind projectKind)
    {
        var context = CreateContext(new FixedPolicyStore(projectKind), MachineCredentialKind.ClientCredentials);

        var exception = await Should.ThrowAsync<SecurityTokenException>(() => MachineCredentialPolicy.EnsureAllowedAsync(context, "org1", "proj1"));

        exception.Message.ShouldContain("org1.proj1");
    }

    [Fact]
    public async Task Policy_ProjectEnablingClientCredentialsAmongOtherKinds_AcceptsTheBearerMachine()
    {
        var policy = new MachineAuthenticationPolicy
        {
            EnabledCredentialKinds = [MachineCredentialKind.ApiKey, MachineCredentialKind.ClientCredentials],
            CredentialKind = MachineCredentialKind.ApiKey,
        };
        var context = CreateContext(new FixedPolicyStore(policy), MachineCredentialKind.ClientCredentials);

        await MachineCredentialPolicy.EnsureAllowedAsync(context, "org1", "proj1");
    }

    [Fact]
    public async Task Policy_ProjectWithSeveralKindsWithoutClientCredentials_RejectsTheBearerMachine()
    {
        var policy = new MachineAuthenticationPolicy { EnabledCredentialKinds = [MachineCredentialKind.ApiKey, MachineCredentialKind.Certificate] };
        var context = CreateContext(new FixedPolicyStore(policy), MachineCredentialKind.ClientCredentials);

        var exception = await Should.ThrowAsync<SecurityTokenException>(() => MachineCredentialPolicy.EnsureAllowedAsync(context, "org1", "proj1"));

        exception.Message.ShouldBe("The project org1.proj1 does not enable client credentials for machines (ApiKey, Certificate).");
    }

    [Theory]
    [InlineData(MachineCredentialKind.ClientCredentials, false)]
    [InlineData(MachineCredentialKind.ApiKey, true)]
    public async Task Policy_WithoutAProjectPolicy_UsesTheDefaultKind(MachineCredentialKind defaultKind, bool rejected)
    {
        var context = CreateContext(new FixedPolicyStore((MachineAuthenticationPolicy?)null), MachineCredentialKind.ClientCredentials, defaultKind);

        var check = () => MachineCredentialPolicy.EnsureAllowedAsync(context, "org1", "proj1");

        if (rejected)
        {
            await Should.ThrowAsync<SecurityTokenException>(check);
        }
        else
        {
            await check();
        }
    }

    [Fact]
    public async Task Policy_FederatedProject_AcceptsTheStorytellerToken()
    {
        var policy = new MachineAuthenticationPolicy { EnabledCredentialKinds = [MachineCredentialKind.ApiKey, MachineCredentialKind.Federated] };
        var context = CreateContext(new FixedPolicyStore(policy), MachineCredentialKind.Federated);

        await MachineCredentialPolicy.EnsureAllowedAsync(context, "org1", "proj1");
    }

    [Fact]
    public async Task Policy_ProjectWithoutFederated_RejectsTheStorytellerToken()
    {
        var policy = new MachineAuthenticationPolicy { EnabledCredentialKinds = [MachineCredentialKind.ApiKey, MachineCredentialKind.ClientCredentials] };
        var context = CreateContext(new FixedPolicyStore(policy), MachineCredentialKind.Federated);

        var exception = await Should.ThrowAsync<SecurityTokenException>(() => MachineCredentialPolicy.EnsureAllowedAsync(context, "org1", "proj1"));

        exception.Message.ShouldBe("The project org1.proj1 does not enable workload identity federation for machines (ApiKey, ClientCredentials).");
    }

    [Fact]
    public async Task Policy_FederatedOnlyProject_RejectsTheClientCredentialsMachine()
    {
        var context = CreateContext(new FixedPolicyStore(MachineCredentialKind.Federated), MachineCredentialKind.ClientCredentials);

        await Should.ThrowAsync<SecurityTokenException>(() => MachineCredentialPolicy.EnsureAllowedAsync(context, "org1", "proj1"));
    }

    [Fact]
    public void IsFederated_OnlyForStorytellerTokens()
    {
        FederatedMachineAccess.IsFederated(CreateContext(new FixedPolicyStore(MachineCredentialKind.Federated), MachineCredentialKind.Federated)).ShouldBeTrue();
        FederatedMachineAccess.IsFederated(CreateContext(new FixedPolicyStore(MachineCredentialKind.Federated), MachineCredentialKind.ClientCredentials)).ShouldBeFalse();
        FederatedMachineAccess.IsFederated(CreateContext(new FixedPolicyStore(MachineCredentialKind.Federated), null)).ShouldBeFalse();
    }

    [Fact]
    public async Task Federated_MachineOfTheTokensProjectWithinItsScope_IsAllowedWithOneRead()
    {
        var access = new FederationAccessService();
        access.Add("org1", "proj1", FederatedMachine(MachineAccessScope.DefaultRead));
        var request = FederatedRequest("org1", "proj1", Scopes.Default.Read, Scopes.Annotation.Read, Scopes.Configuration.Read);

        await FederatedMachineAccess.EnsureAllowedAsync(request, access, "org1", "proj1", FederatedMachineId);

        access.Calls.ShouldBe([$"get:org1/proj1/{FederatedMachineId}"]);
    }

    [Fact]
    public async Task Federated_ProjectNames_CompareIgnoringCase()
    {
        var access = new FederationAccessService();
        access.Add("org1", "proj1", FederatedMachine(MachineAccessScope.DefaultRead));

        await FederatedMachineAccess.EnsureAllowedAsync(FederatedRequest("ORG1", "Proj1", Scopes.Default.Read), access, "org1", "proj1", FederatedMachineId);
    }

    [Theory]
    [InlineData("org1", "proj2")]
    [InlineData("org2", "proj1")]
    public async Task Federated_TokenOfAnotherProject_IsRejectedWithoutARead(string organization, string project)
    {
        var access = new FederationAccessService();
        access.Add(organization, project, FederatedMachine(MachineAccessScope.DefaultRead));
        var request = FederatedRequest("org1", "proj1", Scopes.Default.Read);

        var exception = await Should.ThrowAsync<SecurityTokenException>(
            () => FederatedMachineAccess.EnsureAllowedAsync(request, access, organization, project, FederatedMachineId));

        exception.Message.ShouldContain($"{organization}.{project}");
        access.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Federated_TokenWithoutAProject_IsRejected()
    {
        var access = new FederationAccessService();
        access.Add("org1", "proj1", FederatedMachine(MachineAccessScope.DefaultRead));
        var request = FederatedRequest("org1", "proj1", Scopes.Default.Read);
        request.FunctionContext.Items.Remove(FunctionContextItemKeys.MachineProject);

        await Should.ThrowAsync<SecurityTokenException>(() => FederatedMachineAccess.EnsureAllowedAsync(request, access, "org1", "proj1", FederatedMachineId));
    }

    [Fact]
    public async Task Federated_DeletedMachine_IsRejected()
    {
        var access = new FederationAccessService();

        var exception = await Should.ThrowAsync<SecurityTokenException>(
            () => FederatedMachineAccess.EnsureAllowedAsync(FederatedRequest("org1", "proj1", Scopes.Default.Read), access, "org1", "proj1", FederatedMachineId));

        exception.Message.ShouldBe($"The machine {FederatedMachineId} doesn't exist in the project org1.proj1.");
    }

    // A token signed with a stolen key cannot act as a machine of another kind.
    [Theory]
    [InlineData(MachineCredentialKind.ApiKey)]
    [InlineData(MachineCredentialKind.Certificate)]
    [InlineData(MachineCredentialKind.CertificateAndApiKey)]
    [InlineData(MachineCredentialKind.ClientCredentials)]
    public async Task Federated_StoredMachineOfAnotherKind_IsRejected(MachineCredentialKind kind)
    {
        var access = new FederationAccessService();
        access.Add("org1", "proj1", FederatedMachine(MachineAccessScope.DefaultRead) with { CredentialKind = kind });

        var exception = await Should.ThrowAsync<SecurityTokenException>(
            () => FederatedMachineAccess.EnsureAllowedAsync(FederatedRequest("org1", "proj1", Scopes.Default.Read), access, "org1", "proj1", FederatedMachineId));

        exception.Message.ShouldContain("not Federated");
    }

    // Nor exceed the scope stored with the machine, in any form CheckScope reads.
    [Theory]
    [InlineData("roles", Scopes.Configuration.Secrets)]
    [InlineData("roles", Scopes.Default.Write)]
    [InlineData("roles", "App.Default.ReadWrite")]
    [InlineData("roles", Scopes.User.Impersonation)]
    [InlineData("scp", "Default.Read Configuration.ReadWrite")]
    public async Task Federated_ScopeBeyondTheStoredMachine_IsRejected(string claimType, string value)
    {
        var access = new FederationAccessService();
        access.Add("org1", "proj1", FederatedMachine(MachineAccessScope.DefaultRead));
        var request = FederatedRequest("org1", "proj1", Scopes.Default.Read);
        ((List<Claim>)request.FunctionContext.Items[FunctionContextItemKeys.CachedClaims]).Add(new Claim(claimType, value));

        var exception = await Should.ThrowAsync<SecurityTokenException>(
            () => FederatedMachineAccess.EnsureAllowedAsync(request, access, "org1", "proj1", FederatedMachineId));

        exception.Message.ShouldContain("scopes the machine doesn't have");
    }

    [Fact]
    public async Task CheckAccessTo_FederatedIdentity_ReadsTheMachineInsteadOfVerifyingIt()
    {
        var access = new FederationAccessService();
        access.Add("org1", "proj1", FederatedMachine(MachineAccessScope.DefaultRead));
        var allowed = FederatedRequest("org1", "proj1", Scopes.Default.Read);
        var otherProject = FederatedRequest("org1", "proj2", Scopes.Default.Read);

        await allowed.CheckAccessToProjectAsync(access, "org1", "proj1", AccountRole.Contributor);
        var check = () => otherProject.CheckAccessToProjectAsync(access, "org1", "proj1");

        // The checks of machines run in every build, also in Debug (DEV_AUTH).
        await Should.ThrowAsync<SecurityTokenException>(check);
        access.Calls.ShouldBe([$"get:org1/proj1/{FederatedMachineId}"]);
    }

    [Fact]
    public async Task CheckAccessTo_FederatedIdentityOfAProjectWithoutFederated_IsRejected()
    {
        var access = new FederationAccessService();
        access.Add("org1", "proj1", FederatedMachine(MachineAccessScope.DefaultRead));
        var request = FederatedRequest("org1", "proj1", new FixedPolicyStore(MachineCredentialKind.ClientCredentials), Scopes.Default.Read);

        var check = () => request.CheckAccessToProjectAsync(access, "org1", "proj1");

        var exception = await Should.ThrowAsync<SecurityTokenException>(check);
        exception.Message.ShouldContain("workload identity federation");
    }

    private static TestHttpRequestData FederatedRequest(string organization, string project, params string[] roles)
    {
        return FederatedRequest(organization, project, new FixedPolicyStore(MachineCredentialKind.Federated), roles);
    }

    // The items BearerAuthenticationMiddleware stores for a Storyteller token.
    private static TestHttpRequestData FederatedRequest(string organization, string project, IMachineAuthenticationPolicyStore store, params string[] roles)
    {
        var services = new ServiceCollection()
            .AddSingleton(store)
            .AddSingleton(Options.Create(new MachineAuthenticationOptions()))
            .BuildServiceProvider();
        var (context, request) = FunctionTestDoubles.CreateRequest(services);
        var claims = new List<Claim> { new("azp", FederatedMachineId), new("sub", FederatedMachineId) };
        claims.AddRange(roles.Select(role => new Claim("roles", role)));
        context.Items[FunctionContextItemKeys.CachedClaims] = claims;
        context.Items[FunctionContextItemKeys.MachineIdentity] = FederatedMachineId;
        context.Items[FunctionContextItemKeys.MachineCredentialKind] = MachineCredentialKind.Federated;
        context.Items[FunctionContextItemKeys.MachineOrganization] = organization;
        context.Items[FunctionContextItemKeys.MachineProject] = project;
        return request;
    }

    private static MachineAccess FederatedMachine(MachineAccessScope scope)
    {
        return new MachineAccess
        {
            Id = FederatedMachineId,
            ObjectId = FederatedMachineId,
            AccessKey = string.Empty,
            Scope = scope,
            CredentialKind = MachineCredentialKind.Federated,
        };
    }

    private static TestFunctionContext CreateContext(
        IMachineAuthenticationPolicyStore store,
        MachineCredentialKind? credentialKind,
        MachineCredentialKind defaultKind = MachineCredentialKind.ApiKey)
    {
        var services = new ServiceCollection()
            .AddSingleton(store)
            .AddSingleton(Options.Create(new MachineAuthenticationOptions { DefaultCredentialKind = defaultKind }))
            .BuildServiceProvider();
        var (context, _) = FunctionTestDoubles.CreateRequest(services);

        if (credentialKind is not null)
        {
            context.Items[FunctionContextItemKeys.MachineCredentialKind] = credentialKind.Value;
        }

        return context;
    }

    private sealed class FixedPolicyStore : IMachineAuthenticationPolicyStore
    {
        private readonly MachineAuthenticationPolicy? _policy;

        public FixedPolicyStore(MachineCredentialKind? kind)
            : this(kind is null ? null : new MachineAuthenticationPolicy { CredentialKind = kind.Value })
        {
        }

        public FixedPolicyStore(MachineAuthenticationPolicy? policy)
        {
            _policy = policy;
        }

        public int Reads { get; private set; }

        public Task<MachineAuthenticationPolicy?> GetAsync(string organization, string project)
        {
            Reads++;
            return Task.FromResult(_policy);
        }

        public Task SetAsync(string organization, string project, MachineAuthenticationPolicy policy)
        {
            throw new NotSupportedException();
        }
    }
}
