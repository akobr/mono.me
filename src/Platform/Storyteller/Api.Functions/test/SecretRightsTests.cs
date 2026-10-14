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

        var rights = await request.GetSecretRightsAsync(new RoleAccess(role), new FixedKeyValuePolicies(requireMtls: true), Org, Project);

        rights.CanReadSecrets.ShouldBe(expected);
        rights.Caller.CanWriteSecrets.ShouldBe(expected);
        rights.Caller.Channel.ShouldBe(KeyValueChannel.User);
        rights.Caller.Actor.Kind.ShouldBe(AuditActorKind.Account);
        rights.Caller.Actor.Id.ShouldBe("user-1");
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

        var rights = await request.GetSecretRightsAsync(new RoleAccess(AccountRole.Reader), new FixedKeyValuePolicies(requireMtls: true), Org, Project);

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

        var rights = await request.GetSecretRightsAsync(new RoleAccess(AccountRole.None), new FixedKeyValuePolicies(requireMtls), Org, Project);

        rights.CanReadSecrets.ShouldBe(expectedAllowed);
        rights.IsWithheldByMtls.ShouldBe(expectedWithheld);
        rights.Caller.Channel.ShouldBe(presentsCertificate ? KeyValueChannel.MachineMtls : KeyValueChannel.MachineToken);
        rights.Caller.Actor.ShouldBe(AuditActor.Machine("machine-1"));
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

        var rights = await request.GetSecretRightsAsync(new RoleAccess(AccountRole.None), new FixedKeyValuePolicies(requireMtls: true), Org, Project);

        rights.CanReadSecrets.ShouldBeFalse();
        rights.IsWithheldByMtls.ShouldBeFalse("there was nothing to withhold");
    }
}
