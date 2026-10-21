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

    // One role claim per scope, the scope string of MachineScopes; the checks of the API use these constants.
    [Theory]
    [InlineData(MachineScope.AnnotationRead, Scopes.Annotation.Read)]
    [InlineData(MachineScope.AnnotationWrite, Scopes.Annotation.Write)]
    [InlineData(MachineScope.ConfigurationRead, Scopes.Configuration.Read)]
    [InlineData(MachineScope.ConfigurationWrite, Scopes.Configuration.Write)]
    [InlineData(MachineScope.SecretsRead, Scopes.Secrets.Read)]
    [InlineData(MachineScope.SecretsWrite, Scopes.Secrets.Write)]
    [InlineData(MachineScope.ConfigurationTransfer, Scopes.Configuration.Transfer)]
    [InlineData(MachineScope.SchemaWrite, Scopes.Schema.Write)]
    [InlineData(MachineScope.TemplateWrite, Scopes.Template.Write)]
    [InlineData(MachineScope.AnnotationManage, Scopes.Annotation.Manage)]
    [InlineData(MachineScope.KeyValueManage, Scopes.KeyValue.Manage)]
    public void MachineScopeRoles_MatchTheApiScopeNames(MachineScope scope, string expected)
    {
        MachineScopes.ToScopeString(scope).ShouldBe(expected);
        MachineScopeClaims.GetRoleClaims([scope]).ShouldBe([expected]);
    }

    // MachineScopes keeps its own literals; the checks of the API use these constants.
    [Fact]
    public void ManagementScopes_MatchTheApiScopeNames()
    {
        MachineScopes.Management.Select(scope => MachineScopes.ToScopeString(scope)).ShouldBe(
            [Scopes.Configuration.Transfer, Scopes.Schema.Write, Scopes.Template.Write, Scopes.Annotation.Manage, Scopes.KeyValue.Manage]);
    }

    [Fact]
    public void MachineScopeRoles_AreTheStoredScopesInTheirOrder()
    {
        var roles = MachineScopeClaims.GetRoleClaims(
            [MachineScope.ConfigurationRead, MachineScope.ConfigurationWrite, MachineScope.SchemaWrite, MachineScope.TemplateWrite]);

        roles.ShouldBe([Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Schema.Write, Scopes.Template.Write]);
        MachineScopeClaims.GetRoleClaims([]).ShouldBeEmpty();
        MachineScopeClaims.GetRoleClaims(null).ShouldBeEmpty();
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
        access.Add("org1", "proj1", FederatedMachine());
        var request = FederatedRequest("org1", "proj1", Scopes.Annotation.Read, Scopes.Configuration.Read);

        await FederatedMachineAccess.EnsureAllowedAsync(request, access, "org1", "proj1", FederatedMachineId);

        access.Calls.ShouldBe([$"get:org1/proj1/{FederatedMachineId}"]);
    }

    [Fact]
    public async Task Federated_ProjectNames_CompareIgnoringCase()
    {
        var access = new FederationAccessService();
        access.Add("org1", "proj1", FederatedMachine());

        await FederatedMachineAccess.EnsureAllowedAsync(FederatedRequest("ORG1", "Proj1", Scopes.Annotation.Read, Scopes.Configuration.Read), access, "org1", "proj1", FederatedMachineId);
    }

    [Theory]
    [InlineData("org1", "proj2")]
    [InlineData("org2", "proj1")]
    public async Task Federated_TokenOfAnotherProject_IsRejectedWithoutARead(string organization, string project)
    {
        var access = new FederationAccessService();
        access.Add(organization, project, FederatedMachine());
        var request = FederatedRequest("org1", "proj1", Scopes.Annotation.Read, Scopes.Configuration.Read);

        var exception = await Should.ThrowAsync<SecurityTokenException>(
            () => FederatedMachineAccess.EnsureAllowedAsync(request, access, organization, project, FederatedMachineId));

        exception.Message.ShouldContain($"{organization}.{project}");
        access.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Federated_TokenWithoutAProject_IsRejected()
    {
        var access = new FederationAccessService();
        access.Add("org1", "proj1", FederatedMachine());
        var request = FederatedRequest("org1", "proj1", Scopes.Annotation.Read, Scopes.Configuration.Read);
        request.FunctionContext.Items.Remove(FunctionContextItemKeys.MachineProject);

        await Should.ThrowAsync<SecurityTokenException>(() => FederatedMachineAccess.EnsureAllowedAsync(request, access, "org1", "proj1", FederatedMachineId));
    }

    [Fact]
    public async Task Federated_DeletedMachine_IsRejected()
    {
        var access = new FederationAccessService();

        var exception = await Should.ThrowAsync<SecurityTokenException>(
            () => FederatedMachineAccess.EnsureAllowedAsync(FederatedRequest("org1", "proj1", Scopes.Annotation.Read, Scopes.Configuration.Read), access, "org1", "proj1", FederatedMachineId));

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
        access.Add("org1", "proj1", FederatedMachine() with { CredentialKind = kind });

        var exception = await Should.ThrowAsync<SecurityTokenException>(
            () => FederatedMachineAccess.EnsureAllowedAsync(FederatedRequest("org1", "proj1", Scopes.Annotation.Read, Scopes.Configuration.Read), access, "org1", "proj1", FederatedMachineId));

        exception.Message.ShouldContain("not Federated");
    }

    // The excess check is gone (424-79): a token that carries more than the stored machine, in any form GetScopes reads,
    // still passes an operation that does not need the extra scope. The extra scope grants nothing (the next test).
    [Theory]
    [InlineData("roles", Scopes.Secrets.Read)]
    [InlineData("roles", Scopes.Annotation.Write)]
    [InlineData("roles", "App.Annotation.Write")]
    [InlineData("roles", Scopes.User.Impersonation)]
    [InlineData("roles", Scopes.Configuration.Transfer)]
    [InlineData("roles", "Default.ReadWrite")]
    [InlineData("scp", "Annotation.Read Configuration.Write")]
    [InlineData(ClaimTypes.Role, Scopes.Schema.Write)]
    public async Task Federated_ScopeBeyondTheStoredMachine_StillPassesAnOperationThatDoesNotNeedIt(string claimType, string value)
    {
        var access = new FederationAccessService();
        access.Add("org1", "proj1", FederatedMachine());
        var request = FederatedRequest("org1", "proj1", Scopes.Annotation.Read, Scopes.Configuration.Read);
        Carry(request, claimType, value);

        var machine = await FederatedMachineAccess.EnsureAllowedAsync(request, access, "org1", "proj1", FederatedMachineId);
        await request.CheckAccessToProjectAsync(access, "org1", "proj1", AccountRole.Reader, MachineScope.ConfigurationRead);
        await request.CheckAccessToProjectMetadataAsync(access, "org1", "proj1", AccountRole.Reader, MachineScope.AnnotationRead, MachineScope.ConfigurationRead);

        machine.Scopes.ShouldBe([MachineScope.AnnotationRead, MachineScope.ConfigurationRead]);
    }

    // A carried scope that is not stored is 403 at the operation that needs it, and passes once the machine has it.
    [Theory]
    [InlineData("roles", Scopes.Annotation.Write, MachineScope.AnnotationWrite)]
    [InlineData("roles", "App.Annotation.Write", MachineScope.AnnotationWrite)]
    [InlineData("scp", "Annotation.Read Configuration.Write", MachineScope.ConfigurationWrite)]
    [InlineData(ClaimTypes.Role, Scopes.Secrets.Read, MachineScope.SecretsRead)]
    [InlineData("roles", Scopes.Configuration.Transfer, MachineScope.ConfigurationTransfer)]
    [InlineData("roles", Scopes.Schema.Write, MachineScope.SchemaWrite)]
    public async Task Federated_CarriedScopeTheStoredMachineDoesNotHave_IsAccessDeniedAtItsOperation(string claimType, string value, MachineScope scope)
    {
        var access = new FederationAccessService();
        access.Add("org1", "proj1", FederatedMachine());
        var request = FederatedRequest("org1", "proj1", Scopes.Annotation.Read, Scopes.Configuration.Read);
        Carry(request, claimType, value);
        var check = () => request.CheckAccessToProjectAsync(access, "org1", "proj1", AccountRole.Reader, MachineScope.ConfigurationRead, scope);

        var exception = await Should.ThrowAsync<AccessDeniedException>(check);

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe($"This operation needs the scope {MachineScopes.ToScopeString(scope)}, which the machine was not created with.");

        access.Add("org1", "proj1", FederatedMachine(scope));
        await check();
    }

    // The exchange puts the stored scopes into the token, so each call finds them stored and carried.
    [Fact]
    public async Task Federated_ManagementScopeOfTheStoredMachine_IsAllowed()
    {
        var access = new FederationAccessService();
        access.Add("org1", "proj1", FederatedMachine(MachineScope.ConfigurationTransfer));
        var request = FederatedRequest("org1", "proj1", Scopes.Annotation.Read, Scopes.Configuration.Read, Scopes.Configuration.Transfer);

        var machine = await FederatedMachineAccess.EnsureAllowedAsync(request, access, "org1", "proj1", FederatedMachineId);
        await request.CheckAccessToProjectAsync(access, "org1", "proj1", AccountRole.Reader, MachineScope.ConfigurationRead, MachineScope.ConfigurationTransfer);

        machine.Scopes.ShouldBe([MachineScope.AnnotationRead, MachineScope.ConfigurationRead, MachineScope.ConfigurationTransfer]);
    }

    // User.Impersonation in a Storyteller token does not make the machine a user: the routes for users refuse it in
    // every build, before any read.
    [Fact]
    public async Task Federated_UserImpersonation_DoesNotOpenARouteForUsers()
    {
        var access = new FederationAccessService();
        access.Add("org1", "proj1", FederatedMachine());
        var request = FederatedRequest("org1", "proj1", Scopes.User.Impersonation, Scopes.Annotation.Read, Scopes.Configuration.Read);

        var exceptions = new List<AccessDeniedException>
        {
            Should.Throw<AccessDeniedException>(() => request.EnsureUser()),
            await Should.ThrowAsync<AccessDeniedException>(() => request.CheckUserAccessToAsync(access, "org1.proj1", AccountRole.Reader)),
            await Should.ThrowAsync<AccessDeniedException>(() => request.CheckUserAccessToOrganizationAsync(access, "org1", AccountRole.Reader)),
            await Should.ThrowAsync<AccessDeniedException>(() => request.CheckUserAccessToProjectAsync(access, "org1", "proj1", AccountRole.Reader)),
        };

        exceptions.ShouldAllBe(exception => exception.ErrorCode == ErrorCodes.AccessDenied
            && exception.Message == "Only users may call this operation; machines cannot.");
        access.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task CheckAccessTo_FederatedIdentity_ReadsTheMachineInsteadOfVerifyingIt()
    {
        var access = new FederationAccessService();
        access.Add("org1", "proj1", FederatedMachine());
        var allowed = FederatedRequest("org1", "proj1", Scopes.Annotation.Read, Scopes.Configuration.Read);
        var otherProject = FederatedRequest("org1", "proj2", Scopes.Annotation.Read, Scopes.Configuration.Read);

        await allowed.CheckAccessToProjectAsync(access, "org1", "proj1", AccountRole.Contributor, MachineScope.ConfigurationRead);
        var check = () => otherProject.CheckAccessToProjectAsync(access, "org1", "proj1", AccountRole.Reader, MachineScope.ConfigurationRead);

        // The checks of machines run in every build, also in Debug (DEV_AUTH).
        await Should.ThrowAsync<SecurityTokenException>(check);
        access.Calls.ShouldBe([$"get:org1/proj1/{FederatedMachineId}"]);
    }

    [Fact]
    public async Task CheckAccessTo_FederatedIdentityOfAProjectWithoutFederated_IsRejected()
    {
        var access = new FederationAccessService();
        access.Add("org1", "proj1", FederatedMachine());
        var request = FederatedRequest("org1", "proj1", new FixedPolicyStore(MachineCredentialKind.ClientCredentials), Scopes.Annotation.Read, Scopes.Configuration.Read);

        var check = () => request.CheckAccessToProjectAsync(access, "org1", "proj1", AccountRole.Reader, MachineScope.ConfigurationRead);

        var exception = await Should.ThrowAsync<SecurityTokenException>(check);
        exception.Message.ShouldContain("workload identity federation");
    }

    // A federated machine reaches its document on its own branch (424-57); the scopes of the operation are still checked
    // on that document and on the token of the exchange.
    [Theory]
    [MemberData(nameof(MachineAccessCheckTests.EveryManagementReach), MemberType = typeof(MachineAccessCheckTests))]
    public async Task CheckAccessTo_FederatedMachineWithTheManagementScopeStoredAndCarried_Passes(MachineAccessCheckTests.Reach reach)
    {
        var access = new FederationAccessService();
        access.Add("org1", "proj1", FederatedMachine(MachineScope.ConfigurationTransfer));
        var request = FederatedRequest("org1", "proj1", Scopes.Annotation.Read, Scopes.Configuration.Read, Scopes.Configuration.Transfer);

        await CheckManagement(reach, request, access, MachineScope.ConfigurationTransfer);

        access.Calls.ShouldBe([$"get:org1/proj1/{FederatedMachineId}"]);
    }

    // A token that asked for a subset of the machine's scopes stays within it, for the runtime scopes too.
    [Fact]
    public async Task CheckAccessTo_FederatedTokenOfASubset_IsAccessDeniedOutsideIt()
    {
        var access = new FederationAccessService();
        access.Add("org1", "proj1", FederatedMachine());
        var request = FederatedRequest("org1", "proj1", Scopes.Annotation.Read);

        await request.CheckAccessToAnnotationAsync(access, "org1", "proj1", "exe.S.R.C", AccountRole.Reader, MachineScope.AnnotationRead);
        var exception = await Should.ThrowAsync<AccessDeniedException>(
            () => request.CheckAccessToProjectAsync(access, "org1", "proj1", AccountRole.Reader, MachineScope.ConfigurationRead));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("This operation needs the scope Configuration.Read, which the credentials of the machine do not carry.");
    }

    // A token that asked for a subset of the machine's scopes leaves the management scope out.
    [Theory]
    [MemberData(nameof(MachineAccessCheckTests.EveryManagementReach), MemberType = typeof(MachineAccessCheckTests))]
    public async Task CheckAccessTo_FederatedMachineWithoutTheCarriedManagementScope_IsAccessDenied(MachineAccessCheckTests.Reach reach)
    {
        var access = new FederationAccessService();
        access.Add("org1", "proj1", FederatedMachine(MachineScope.ConfigurationTransfer));
        var request = FederatedRequest("org1", "proj1", Scopes.Annotation.Read, Scopes.Configuration.Read);

        var exception = await Should.ThrowAsync<AccessDeniedException>(
            () => CheckManagement(reach, request, access, MachineScope.ConfigurationTransfer));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("This operation needs the scope Configuration.Transfer, which the credentials of the machine do not carry.");
    }

    [Theory]
    [MemberData(nameof(MachineAccessCheckTests.EveryManagementReach), MemberType = typeof(MachineAccessCheckTests))]
    public async Task CheckAccessTo_FederatedMachineWithoutTheStoredManagementScope_IsAccessDenied(MachineAccessCheckTests.Reach reach)
    {
        var access = new FederationAccessService();
        access.Add("org1", "proj1", FederatedMachine());
        var request = FederatedRequest("org1", "proj1", Scopes.Annotation.Read, Scopes.Configuration.Read);

        var exception = await Should.ThrowAsync<AccessDeniedException>(
            () => CheckManagement(reach, request, access, MachineScope.ConfigurationTransfer));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("This operation needs the scope Configuration.Transfer, which the machine was not created with.");
    }

    // The entry points of a management operation, in org1.proj1: the project, one annotation and several, with the runtime
    // scope Configuration.Read first and then the management scope.
    private static Task CheckManagement(
        MachineAccessCheckTests.Reach reach,
        TestHttpRequestData request,
        IAccessService access,
        MachineScope managementScope)
    {
        return reach switch
        {
            MachineAccessCheckTests.Reach.Project => request.CheckAccessToProjectAsync(access, "org1", "proj1", AccountRole.Contributor, MachineScope.ConfigurationRead, managementScope),
            MachineAccessCheckTests.Reach.Annotation => request.CheckAccessToAnnotationAsync(access, "org1", "proj1", "exe.S.R.C", AccountRole.Contributor, MachineScope.ConfigurationRead, managementScope),
            MachineAccessCheckTests.Reach.Annotations => request.CheckAccessToAnnotationsAsync(access, "org1", "proj1", ["exe.S.R.C", "uxe.S.R.C.U"], AccountRole.Contributor, MachineScope.ConfigurationRead, managementScope),
            _ => throw new ArgumentOutOfRangeException(nameof(reach), reach, null),
        };
    }

    // One more claim in the token, next to its roles.
    private static void Carry(TestHttpRequestData request, string claimType, string value)
    {
        ((List<Claim>)request.FunctionContext.Items[FunctionContextItemKeys.CachedClaims]).Add(new Claim(claimType, value));
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

    // A reader of the project (Annotation.Read and Configuration.Read), with the given scopes on top.
    private static MachineAccess FederatedMachine(params MachineScope[] moreScopes)
    {
        return new MachineAccess
        {
            Id = FederatedMachineId,
            ObjectId = FederatedMachineId,
            AccessKey = string.Empty,
            Scopes = [MachineScope.AnnotationRead, MachineScope.ConfigurationRead, .. moreScopes],
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
