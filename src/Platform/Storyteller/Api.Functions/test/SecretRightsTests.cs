using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Api;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;
using _42.Platform.Storyteller.KeyValues;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// Who may receive key/value secrets: the role of a user, the secrets scopes of a machine, and the mTLS rule of the
// project. A machine reads secret values with Secrets.Read and writes them with Secrets.Write, each stored on the machine
// and carried by its credentials, in every build. A user gets both from ContributorWithSecrets and from nothing else; an
// actor under NoSecretReveal withholds the read right of the user it acts for, in every build.
public class SecretRightsTests
{
    private const string Org = "acme";
    private const string Project = "billing";

    // Debug builds (DEV_AUTH) skip the checks of users, so a Debug user gets secret rights without the role.
    private static bool DevAuth => HttpRequestDataExtensions.IsDevelopmentAuthentication;

    [Theory]
    [InlineData(AccountRole.Reader, false)]
    [InlineData(AccountRole.Contributor, false)]
    [InlineData(AccountRole.ContributorWithSecrets, true)]
    [InlineData(AccountRole.Administrator, true)]
    public async Task User_NeedsContributorWithSecrets(AccountRole role, bool expected)
    {
        if (DevAuth && !expected)
        {
            // a Debug build skips the role check of users and grants them secret rights
            return;
        }

        var request = TestRequests.User();

        var rights = await request.GetSecretRightsAsync(new RoleAccess(role), new FixedKeyValuePolicies(requireMtls: true), Org, Project);

        rights.CanReadSecrets.ShouldBe(expected);
        rights.CanWriteSecrets.ShouldBe(expected);
        rights.Caller.Channel.ShouldBe(KeyValueChannel.User);
        rights.Caller.Actor.Kind.ShouldBe(AuditActorKind.Account);
        rights.Caller.Actor.Id.ShouldBe(TestRequests.UserId);
        rights.IsReadWithheldByMtls.ShouldBeFalse();
        rights.IsWriteWithheldByMtls.ShouldBeFalse();
        rights.IsReadWithheldByActor.ShouldBeFalse();
        rights.Caller.Actor.Via.ShouldBeNull();
        rights.AsAmbient().ShouldBeSameAs(rights);
        KeyValueCallerContext.Current.ShouldBe(rights.Caller);
    }

    // The secrets scopes describe machines: in a user token they give nothing, the role decides.
    [Theory]
    [InlineData(AccountRole.Reader)]
    [InlineData(AccountRole.Contributor)]
    public async Task User_WithTheSecretsScopesButWithoutTheRole_GetsNothing(AccountRole role)
    {
        if (DevAuth)
        {
            return;
        }

        var request = TestRequests.UserCarrying([Scopes.User.Impersonation, Scopes.Secrets.Read, Scopes.Secrets.Write]);

        var rights = await request.GetSecretRightsAsync(new RoleAccess(role), new FixedKeyValuePolicies(requireMtls: false), Org, Project);

        rights.CanReadSecrets.ShouldBeFalse();
        rights.CanWriteSecrets.ShouldBeFalse();
        rights.IsReadWithheldByMtls.ShouldBeFalse();
        rights.IsWriteWithheldByMtls.ShouldBeFalse();
    }

    // A token that does not act for a user has no account to look up; the access check refused it already.
    [Fact]
    public async Task User_WithoutUserImpersonation_GetsNothing()
    {
        if (DevAuth)
        {
            return;
        }

        var request = TestRequests.UserCarrying([Scopes.Secrets.Read, Scopes.Secrets.Write]);

        var rights = await request.GetSecretRightsAsync(new RoleAccess(AccountRole.Owner), new FixedKeyValuePolicies(requireMtls: false), Org, Project);

        rights.CanReadSecrets.ShouldBeFalse();
        rights.CanWriteSecrets.ShouldBeFalse();
    }

    // An actor under NoSecretReveal withholds the read right the role gives and says so; the write right stays. The
    // restriction runs in every build: Debug (DEV_AUTH) gives every user the rights and the actor still withholds reads.
    [Theory]
    [InlineData(AccountRole.ContributorWithSecrets)]
    [InlineData(AccountRole.Administrator)]
    [InlineData(AccountRole.Owner)]
    public async Task DelegatedUser_UnderNoSecretReveal_IsWithheldTheReadRightOnly(AccountRole role)
    {
        var request = TestRequests.DelegatedUser([ActorRestrictions.NoSecretReveal]);

        var rights = await request.GetSecretRightsAsync(new RoleAccess(role), new FixedKeyValuePolicies(requireMtls: true), Org, Project);

        rights.CanReadSecrets.ShouldBeFalse();
        rights.CanWriteSecrets.ShouldBeTrue();
        rights.IsReadWithheldByActor.ShouldBeTrue();
        rights.IsReadWithheldByMtls.ShouldBeFalse();
        rights.IsWriteWithheldByMtls.ShouldBeFalse();
        rights.Caller.Channel.ShouldBe(KeyValueChannel.User);
        rights.Caller.Actor.Via.ShouldBe(TestRequests.ActorId);
    }

    // An actor that allows secrets (AllowSecrets: no NoSecretReveal in the token) leaves the user the rights of the role.
    [Fact]
    public async Task DelegatedUser_ThroughAnActorThatAllowsSecrets_KeepsBothRights()
    {
        var request = TestRequests.DelegatedUser([]);

        var rights = await request.GetSecretRightsAsync(new RoleAccess(AccountRole.ContributorWithSecrets), new FixedKeyValuePolicies(requireMtls: true), Org, Project);

        rights.CanReadSecrets.ShouldBeTrue();
        rights.CanWriteSecrets.ShouldBeTrue();
        rights.IsReadWithheldByActor.ShouldBeFalse();
    }

    // Without the role there is no read right to withhold, so a delegated Reader is refused like a direct one
    // (AccessDenied), not for the actor. Debug builds (DEV_AUTH) give every user the rights, so there the actor withholds
    // the read right of a Reader too.
    [Theory]
    [InlineData(AccountRole.Reader)]
    [InlineData(AccountRole.Contributor)]
    public async Task DelegatedUser_UnderNoSecretRevealWithoutTheRole_IsRefusedTheRead(AccountRole role)
    {
        var request = TestRequests.DelegatedUser([ActorRestrictions.NoSecretReveal]);

        var rights = await request.GetSecretRightsAsync(new RoleAccess(role), new FixedKeyValuePolicies(requireMtls: true), Org, Project);

        rights.CanReadSecrets.ShouldBeFalse();
        rights.CanWriteSecrets.ShouldBe(DevAuth);
        rights.IsReadWithheldByActor.ShouldBe(DevAuth);
        Should.Throw<AccessDeniedException>(() => KeyValueAuthorization.EnsureSecretReadRights(rights))
            .ErrorCode.ShouldBe(DevAuth ? ErrorCodes.ActorRestricted : ErrorCodes.AccessDenied);
    }

    // The refusals name the actor for reads only, by the handler and by the service; writes are not withheld, and a
    // refusal of a write by the service stays AccessDenied.
    [Fact]
    public async Task DelegatedUser_UnderNoSecretReveal_IsRefusedForTheActorOnlyForReads()
    {
        var rights = await TestRequests.DelegatedUser().GetSecretRightsAsync(new RoleAccess(AccountRole.ContributorWithSecrets), new FixedKeyValuePolicies(), Org, Project);

        var read = Should.Throw<AccessDeniedException>(() => KeyValueAuthorization.EnsureSecretReadRights(rights));
        var readByTheService = await Should.ThrowAsync<AccessDeniedException>(() => KeyValueAuthorization.UnderSecretReadRuleAsync<bool>(rights, RefusedByTheService));
        var writeByTheService = await Should.ThrowAsync<AccessDeniedException>(() => KeyValueAuthorization.UnderMtlsWriteRuleAsync<bool>(rights, RefusedByTheService));

        read.ErrorCode.ShouldBe(ErrorCodes.ActorRestricted);
        read.Message.ShouldBe(KeyValueErrors.ActorRestricted().Message);
        readByTheService.ErrorCode.ShouldBe(ErrorCodes.ActorRestricted);
        writeByTheService.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        Should.NotThrow(() => KeyValueAuthorization.EnsureSecretWriteRights(rights));
        (await KeyValueAuthorization.UnderSecretReadRuleAsync(rights, () => Task.FromResult(true))).ShouldBeTrue();
    }

    // Restrictions narrow what a user may do through an actor; a machine never acts through one, and stray delegation
    // items do not change its rights.
    [Fact]
    public async Task Machine_WithDelegationItems_IsNotWithheldByAnActor()
    {
        var request = TestRequests.Machine([MachineScope.ConfigurationRead, MachineScope.SecretsRead, MachineScope.SecretsWrite], mtls: true);
        request.FunctionContext.Items[FunctionContextItemKeys.DelegatedRestrictions] = new[] { ActorRestrictions.NoSecretReveal };

        var rights = await request.GetSecretRightsAsync(new RoleAccess(AccountRole.None), new FixedKeyValuePolicies(requireMtls: true), Org, Project);

        rights.CanReadSecrets.ShouldBeTrue();
        rights.CanWriteSecrets.ShouldBeTrue();
        rights.IsReadWithheldByActor.ShouldBeFalse();
    }

    // Read and write are separate rights, each from its own scope stored on the machine and carried by its credentials.
    // The project does not require mTLS here, so nothing is withheld.
    [Theory]
    [InlineData(new[] { MachineScope.SecretsRead }, new[] { MachineScope.SecretsRead }, true, false)]
    [InlineData(new[] { MachineScope.SecretsWrite }, new[] { MachineScope.SecretsWrite }, false, true)]
    [InlineData(new[] { MachineScope.SecretsRead, MachineScope.SecretsWrite }, new[] { MachineScope.SecretsRead, MachineScope.SecretsWrite }, true, true)]
    [InlineData(new[] { MachineScope.SecretsRead, MachineScope.SecretsWrite }, new[] { MachineScope.SecretsRead }, true, false)]
    [InlineData(new[] { MachineScope.SecretsRead, MachineScope.SecretsWrite }, new[] { MachineScope.SecretsWrite }, false, true)]
    [InlineData(new[] { MachineScope.SecretsRead }, new[] { MachineScope.SecretsRead, MachineScope.SecretsWrite }, true, false)]
    [InlineData(new MachineScope[0], new[] { MachineScope.SecretsRead, MachineScope.SecretsWrite }, false, false)]
    [InlineData(new[] { MachineScope.SecretsRead, MachineScope.SecretsWrite }, new MachineScope[0], false, false)]
    public async Task Machine_GetsEachSecretRightFromItsScopeStoredAndCarried(MachineScope[] stored, MachineScope[] carried, bool canRead, bool canWrite)
    {
        var access = new RoleAccess(AccountRole.None, scopes: [MachineScope.ConfigurationRead, MachineScope.ConfigurationWrite, .. stored]);
        var request = TestRequests.Machine([MachineScope.ConfigurationRead, MachineScope.ConfigurationWrite, .. carried]);

        var rights = await request.GetSecretRightsAsync(access, new FixedKeyValuePolicies(requireMtls: false), Org, Project);

        rights.CanReadSecrets.ShouldBe(canRead);
        rights.CanWriteSecrets.ShouldBe(canWrite);
        rights.IsReadWithheldByMtls.ShouldBeFalse();
        rights.IsWriteWithheldByMtls.ShouldBeFalse();
        rights.Caller.Channel.ShouldBe(KeyValueChannel.MachineToken);
        rights.Caller.Actor.ShouldBe(AuditActor.Machine(TestRequests.MachineId));
    }

    // A project that delivers secrets only over mTLS withholds both rights from a machine without a certificate, and
    // says so; with one, the machine keeps what its scopes give.
    [Theory]
    [InlineData(true, false, false, false, true)]
    [InlineData(true, true, true, true, false)]
    [InlineData(false, false, true, true, false)]
    [InlineData(false, true, true, true, false)]
    public async Task Machine_NeedsMtlsWhenTheProjectSaysSo(bool requireMtls, bool presentsCertificate, bool canRead, bool canWrite, bool withheld)
    {
        var request = TestRequests.Machine([MachineScope.ConfigurationRead, MachineScope.SecretsRead, MachineScope.SecretsWrite], mtls: presentsCertificate);

        var rights = await request.GetSecretRightsAsync(new RoleAccess(AccountRole.None), new FixedKeyValuePolicies(requireMtls), Org, Project);

        rights.CanReadSecrets.ShouldBe(canRead);
        rights.CanWriteSecrets.ShouldBe(canWrite);
        rights.IsReadWithheldByMtls.ShouldBe(withheld);
        rights.IsWriteWithheldByMtls.ShouldBe(withheld);
        rights.IsReadWithheldByActor.ShouldBeFalse();
        rights.Caller.Channel.ShouldBe(presentsCertificate ? KeyValueChannel.MachineMtls : KeyValueChannel.MachineToken);
        rights.Caller.Actor.ShouldBe(AuditActor.Machine(TestRequests.MachineId));
    }

    // Either right alone is withheld too, and only that one is reported: mTLS would not give the machine the other.
    [Theory]
    [InlineData(MachineScope.SecretsRead, true, false)]
    [InlineData(MachineScope.SecretsWrite, false, true)]
    public async Task Machine_WithOneSecretsScopeWithoutMtls_IsWithheldThatRightOnly(MachineScope scope, bool readWithheld, bool writeWithheld)
    {
        var access = new RoleAccess(AccountRole.None, scopes: [MachineScope.ConfigurationRead, scope]);

        var rights = await TestRequests.Machine().GetSecretRightsAsync(access, new FixedKeyValuePolicies(requireMtls: true), Org, Project);

        rights.CanReadSecrets.ShouldBeFalse();
        rights.CanWriteSecrets.ShouldBeFalse();
        rights.IsReadWithheldByMtls.ShouldBe(readWithheld);
        rights.IsWriteWithheldByMtls.ShouldBe(writeWithheld);
    }

    // The refusal of each direction names mTLS only when the machine has that right: a machine with Secrets.Read alone
    // is MtlsRequired for a secret read and AccessDenied for a secret write, and Secrets.Write alone the other way round.
    [Theory]
    [InlineData(MachineScope.SecretsRead, ErrorCodes.MtlsRequired, ErrorCodes.AccessDenied)]
    [InlineData(MachineScope.SecretsWrite, ErrorCodes.AccessDenied, ErrorCodes.MtlsRequired)]
    public async Task Machine_WithOneSecretsScopeWithoutMtls_IsRefusedForMtlsOnlyInTheDirectionOfItsScope(MachineScope scope, string readError, string writeError)
    {
        var access = new RoleAccess(AccountRole.None, scopes: [MachineScope.ConfigurationRead, scope]);
        var rights = await TestRequests.Machine().GetSecretRightsAsync(access, new FixedKeyValuePolicies(requireMtls: true), Org, Project);

        var read = Should.Throw<AccessDeniedException>(() => KeyValueAuthorization.EnsureSecretReadRights(rights));
        var write = Should.Throw<AccessDeniedException>(() => KeyValueAuthorization.EnsureSecretWriteRights(rights));
        var readByTheService = await Should.ThrowAsync<AccessDeniedException>(() => KeyValueAuthorization.UnderSecretReadRuleAsync<bool>(rights, RefusedByTheService));
        var writeByTheService = await Should.ThrowAsync<AccessDeniedException>(() => KeyValueAuthorization.UnderMtlsWriteRuleAsync<bool>(rights, RefusedByTheService));

        read.ErrorCode.ShouldBe(readError);
        readByTheService.ErrorCode.ShouldBe(readError);
        write.ErrorCode.ShouldBe(writeError);
        writeByTheService.ErrorCode.ShouldBe(writeError);
    }

    [Fact]
    public async Task Machine_WithoutTheScopes_GetsNothingAndNothingIsWithheld()
    {
        var request = TestRequests.Machine([MachineScope.ConfigurationRead]);

        var rights = await request.GetSecretRightsAsync(new RoleAccess(AccountRole.None), new FixedKeyValuePolicies(requireMtls: true), Org, Project);

        rights.CanReadSecrets.ShouldBeFalse();
        rights.CanWriteSecrets.ShouldBeFalse();
        rights.IsReadWithheldByMtls.ShouldBeFalse("there was nothing to withhold");
        rights.IsWriteWithheldByMtls.ShouldBeFalse("there was nothing to withhold");
    }

    // The secret rights reuse the machine of the access check of the request: one point read for both.
    [Fact]
    public async Task Machine_AfterTheAccessCheck_ReusesItsStoredMachine()
    {
        var (access, documents) = MachineAccessCheckTests.MachineDocuments.Create(new MachineAccess
        {
            Id = TestRequests.MachineId,
            ObjectId = TestRequests.MachineId,
            AccessKey = "***",
            Scopes = [MachineScope.ConfigurationRead, MachineScope.SecretsRead],
        });
        var request = TestRequests.Machine();

        await request.CheckAccessToProjectAsync(access, Org, Project, AccountRole.Reader, MachineScope.ConfigurationRead);
        var rights = await request.GetSecretRightsAsync(access, new FixedKeyValuePolicies(requireMtls: false), Org, Project);

        rights.CanReadSecrets.ShouldBeTrue();
        rights.CanWriteSecrets.ShouldBeFalse();
        documents.Reads.ShouldBe([$"{Org}/{Project}/{TestRequests.MachineId}"]);
    }

    // A machine the project does not know gets nothing; the access check answers 401 for it.
    [Fact]
    public async Task Machine_WithoutItsDocument_GetsNothing()
    {
        var (access, _) = MachineAccessCheckTests.MachineDocuments.Create(machine: null);

        var rights = await TestRequests.Machine().GetSecretRightsAsync(access, new FixedKeyValuePolicies(requireMtls: false), Org, Project);

        rights.CanReadSecrets.ShouldBeFalse();
        rights.CanWriteSecrets.ShouldBeFalse();
    }

    // The service refuses a secret the way the key/value service does.
    private static Task<bool> RefusedByTheService()
    {
        return Task.FromException<bool>(KeyValueErrors.SecretRightsRequired("db.password"));
    }
}
