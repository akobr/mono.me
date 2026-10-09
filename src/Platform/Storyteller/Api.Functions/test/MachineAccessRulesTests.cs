using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api;
using _42.Platform.Storyteller.Api.Security;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class MachineAccessRulesTests
{
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

    [Theory]
    [InlineData(MachineCredentialKind.ClientCredentials, false)]
    [InlineData(MachineCredentialKind.ApiKey, true)]
    public async Task Policy_WithoutAProjectPolicy_UsesTheDefaultKind(MachineCredentialKind defaultKind, bool rejected)
    {
        var context = CreateContext(new FixedPolicyStore(null), MachineCredentialKind.ClientCredentials, defaultKind);

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
        private readonly MachineCredentialKind? _kind;

        public FixedPolicyStore(MachineCredentialKind? kind)
        {
            _kind = kind;
        }

        public int Reads { get; private set; }

        public Task<MachineAuthenticationPolicy?> GetAsync(string organization, string project)
        {
            Reads++;
            return Task.FromResult(_kind is null ? null : new MachineAuthenticationPolicy { CredentialKind = _kind.Value });
        }

        public Task SetAsync(string organization, string project, MachineAuthenticationPolicy policy)
        {
            throw new NotSupportedException();
        }
    }
}
