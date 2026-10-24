using System.Reflection;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api;
using _42.Platform.Storyteller.Api.Security;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The access checks of machine requests: the machine document of the project (401 without it), the credential policy,
// what the operation reaches against the annotation restriction of the machine (403 AnnotationRestricted), and the
// scopes of the operation, stored on the machine and carried by its credentials (403 AccessDenied). The machine branch
// runs in every build, so these tests run in Debug (DEV_AUTH) too, and so does the guard that keeps machines off the
// routes for users. The checks of users, User.Impersonation (401) and the role (403), run in Release only.
public class MachineAccessCheckTests
{
    private const string Organization = "acme";
    private const string Project = "web";
    private const string MachineId = "machine-1";

    // A machine restricted to one execution, and a unit of that execution.
    private const string Restriction = "exe.S.R.C";
    private const string Descendant = "uxe.S.R.C.U";

    public enum Reach
    {
        Project,
        AccessPoint,
        Annotation,
        Annotations,
        Metadata,
    }

    // The reaches of a machine; the access point of the project is a route for users only.
    public static TheoryData<Reach> EveryReach => new()
    {
        Reach.Project,
        Reach.Annotation,
        Reach.Annotations,
        Reach.Metadata,
    };

    public static TheoryData<Reach> EveryManagementReach => new()
    {
        Reach.Project,
        Reach.Annotation,
        Reach.Annotations,
    };

    // A user role one step below the minimal role of the operation.
    public static TheoryData<Reach, AccountRole, AccountRole> UserBelowTheMinimalRole => Users(
        (AccountRole.Reader, AccountRole.Contributor),
        (AccountRole.Contributor, AccountRole.Administrator));

    public static TheoryData<Reach, AccountRole, AccountRole> UserWithTheMinimalRole => Users(
        (AccountRole.Contributor, AccountRole.Contributor),
        (AccountRole.Administrator, AccountRole.Administrator));

    // Debug builds (DEV_AUTH) skip the checks of users: User.Impersonation and the role.
    private static bool DevAuth => HttpRequestDataExtensions.IsDevelopmentAuthentication;

    [Theory]
    [MemberData(nameof(EveryReach))]
    public async Task UnrestrictedMachine_PassesEveryCheck(Reach reach)
    {
        await Check(reach, MachineRequest(), new RoleAccess(AccountRole.Reader));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MachineWithABlankKey_IsUnrestricted(string annotationKey)
    {
        await MachineRequest().CheckAccessToProjectAsync(new RoleAccess(AccountRole.Reader, annotationKey), Organization, Project, AccountRole.Reader, MachineScope.ConfigurationRead);
    }

    // Without a restriction the key is not looked at; the handler answers 400 for a key that does not parse.
    [Theory]
    [InlineData("rst.R")]
    [InlineData("not-a-key")]
    public async Task UnrestrictedMachine_PassesAnyAnnotationKey(string annotationKey)
    {
        var access = new RoleAccess(AccountRole.Reader);

        await MachineRequest().CheckAccessToAnnotationAsync(access, Organization, Project, annotationKey, AccountRole.Reader, MachineScope.ConfigurationRead);
        await MachineRequest().CheckAccessToAnnotationsAsync(access, Organization, Project, [annotationKey, null], AccountRole.Reader, MachineScope.ConfigurationRead);
    }

    // The type code is compared in any case and empty segments are dropped, the way the handler parses the key.
    [Theory]
    [InlineData(Restriction)]
    [InlineData(Descendant)]
    [InlineData("uxe.S.R.C.V")]
    [InlineData("EXE.S.R.C")]
    [InlineData("exe..S.R.C")]
    [InlineData("UXE.S.R.C.U")]
    public async Task RestrictedMachine_PassesItsAnnotationAndDescendants(string annotationKey)
    {
        await MachineRequest().CheckAccessToAnnotationAsync(Restricted(), Organization, Project, annotationKey, AccountRole.Reader, MachineScope.ConfigurationRead);
    }

    [Theory]
    [InlineData("usg.S.R")]
    [InlineData("cnt.S.C")]
    [InlineData("rst.R")]
    [InlineData("sbt.S")]
    public async Task RestrictedMachine_Ancestor_IsRestricted(string annotationKey)
    {
        await ShouldBeRestricted(() => MachineRequest().CheckAccessToAnnotationAsync(Restricted(), Organization, Project, annotationKey, AccountRole.Reader, MachineScope.ConfigurationRead));
    }

    // Names are case sensitive, so another case is another annotation.
    [Theory]
    [InlineData("exe.S.R.D")]
    [InlineData("exe.S.Q.C")]
    [InlineData("exe.T.R.C")]
    [InlineData("uxe.S.R.D.U")]
    [InlineData("exe.s.r.c")]
    public async Task RestrictedMachine_SiblingOrAnotherBranch_IsRestricted(string annotationKey)
    {
        await ShouldBeRestricted(() => MachineRequest().CheckAccessToAnnotationAsync(Restricted(), Organization, Project, annotationKey, AccountRole.Reader, MachineScope.ConfigurationRead));
    }

    [Theory]
    [InlineData("exe.S.R")]
    [InlineData("xyz.S.R.C")]
    [InlineData("not-a-key")]
    [InlineData("")]
    public async Task RestrictedMachine_KeyThatDoesNotParse_IsRestricted(string annotationKey)
    {
        await ShouldBeRestricted(() => MachineRequest().CheckAccessToAnnotationAsync(Restricted(), Organization, Project, annotationKey, AccountRole.Reader, MachineScope.ConfigurationRead));
    }

    [Fact]
    public async Task RestrictedMachine_ProjectCheck_IsRestricted()
    {
        await ShouldBeRestricted(() => MachineRequest().CheckAccessToProjectAsync(Restricted(), Organization, Project, AccountRole.Reader, MachineScope.ConfigurationRead));
    }

    [Fact]
    public async Task RestrictedMachine_BulkInsideItsRestriction_Passes()
    {
        await MachineRequest().CheckAccessToAnnotationsAsync(Restricted(), Organization, Project, [Restriction, Descendant, "UXE.S.R.C.V"], AccountRole.Reader, MachineScope.ConfigurationRead);
    }

    [Theory]
    [InlineData("usg.S.R")]
    [InlineData("exe.S.R.D")]
    [InlineData("not-a-key")]
    [InlineData(null)]
    public async Task RestrictedMachine_BulkWithOneKeyOutside_IsRestricted(string? outside)
    {
        await ShouldBeRestricted(() => MachineRequest().CheckAccessToAnnotationsAsync(Restricted(), Organization, Project, [Restriction, outside, Descendant], AccountRole.Reader, MachineScope.ConfigurationRead));
    }

    [Fact]
    public async Task RestrictedMachine_PassesTheProjectMetadata()
    {
        await MachineRequest().CheckAccessToProjectMetadataAsync(Restricted(), Organization, Project, AccountRole.Reader, MachineScope.ConfigurationRead);
    }

    // Machines created before the key was normalized keep the key as it was sent.
    [Fact]
    public async Task RestrictedMachine_StoredKeyInAnotherForm_IsNormalized()
    {
        var access = new RoleAccess(AccountRole.Reader, "EXE..S.R.C");

        await MachineRequest().CheckAccessToAnnotationAsync(access, Organization, Project, Restriction, AccountRole.Reader, MachineScope.ConfigurationRead);
        await MachineRequest().CheckAccessToAnnotationAsync(access, Organization, Project, Descendant, AccountRole.Reader, MachineScope.ConfigurationRead);
        await ShouldBeRestricted(() => MachineRequest().CheckAccessToAnnotationAsync(access, Organization, Project, "usg.S.R", AccountRole.Reader, MachineScope.ConfigurationRead));
    }

    // A stored key that does not parse restricts the machine to nothing, not even its own text.
    [Fact]
    public async Task MachineWithAKeyThatDoesNotParse_ReachesNoAnnotation()
    {
        var access = new RoleAccess(AccountRole.Reader, "exe.S.R");

        await ShouldBeRestricted(() => MachineRequest().CheckAccessToAnnotationAsync(access, Organization, Project, "exe.S.R", AccountRole.Reader, MachineScope.ConfigurationRead));
        await ShouldBeRestricted(() => MachineRequest().CheckAccessToAnnotationsAsync(access, Organization, Project, [Restriction], AccountRole.Reader, MachineScope.ConfigurationRead));
        await ShouldBeRestricted(() => MachineRequest().CheckAccessToProjectAsync(access, Organization, Project, AccountRole.Reader, MachineScope.ConfigurationRead));
    }

    // Without its document the machine is not a machine of the project, whatever the operation reaches.
    [Theory]
    [MemberData(nameof(EveryReach))]
    public async Task MissingMachine_IsUnauthorized(Reach reach)
    {
        var (access, documents) = MachineDocuments.Create(machine: null);

        await Should.ThrowAsync<SecurityTokenException>(() => Check(reach, MachineRequest(), access));

        documents.Reads.ShouldBe([$"{Organization}/{Project}/{MachineId}"]);
    }

    // An unknown machine gets 401 before any 403, also when its credentials carry no scope of the operation.
    [Theory]
    [MemberData(nameof(EveryReach))]
    public async Task MissingMachine_WithoutTheScope_IsUnauthorizedFirst(Reach reach)
    {
        var (access, _) = MachineDocuments.Create(machine: null);

        await Should.ThrowAsync<SecurityTokenException>(() => Check(reach, TestRequests.Machine([]), access, scope: MachineScope.AnnotationWrite));
    }

    // The access points (an organization, or organization.project) are routes for users only: a machine is refused
    // before any read, in every build, restricted or not.
    [Theory]
    [InlineData(null)]
    [InlineData(Restriction)]
    public async Task Machine_AccessPoints_AreForUsersOnly(string? annotationKey)
    {
        var (access, documents) = MachineDocuments.Create(Machine(annotationKey));

        await ShouldBeForUsersOnly(() => MachineRequest().CheckUserAccessToAsync(access, Organization, AccountRole.Reader));
        await ShouldBeForUsersOnly(() => MachineRequest().CheckUserAccessToAsync(access, $"{Organization}.{Project}", AccountRole.Reader));
        await ShouldBeForUsersOnly(() => MachineRequest().CheckUserAccessToOrganizationAsync(access, Organization, AccountRole.Reader));
        await ShouldBeForUsersOnly(() => MachineRequest().CheckUserAccessToProjectAsync(access, Organization, Project, AccountRole.Reader));

        documents.Reads.ShouldBeEmpty();
    }

    // User.Impersonation in the credentials of a machine does not make it a user.
    [Fact]
    public async Task MachineCarryingUserImpersonation_IsStillRefusedByTheRoutesForUsers()
    {
        var (access, documents) = MachineDocuments.Create(Machine(annotationKey: null));
        var request = TestRequests.MachineCarrying([Scopes.User.Impersonation, .. MachineScopes.ToScopeStrings(MachineScopes.All)]);

        await ShouldBeForUsersOnly(() =>
        {
            request.EnsureUser();
            return Task.CompletedTask;
        });
        await ShouldBeForUsersOnly(() => request.CheckUserAccessToAsync(access, $"{Organization}.{Project}", AccountRole.Reader));
        await ShouldBeForUsersOnly(() => request.CheckUserAccessToOrganizationAsync(access, Organization, AccountRole.Reader));
        await ShouldBeForUsersOnly(() => request.CheckUserAccessToProjectAsync(access, Organization, Project, AccountRole.Reader));

        documents.Reads.ShouldBeEmpty();
    }

    [Fact]
    public void EnsureUser_Machine_GetsTheMessageOfTheRoute()
    {
        var exception = Should.Throw<AccessDeniedException>(() => MachineRequest().EnsureUser("Machines cannot purge annotations."));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("Machines cannot purge annotations.");
    }

    // One read of the machine document, by the machine id of the request; never an account role.
    [Theory]
    [MemberData(nameof(EveryReach))]
    public async Task Machine_ReadsItsDocumentOfTheProject(Reach reach)
    {
        var (access, documents) = MachineDocuments.Create(Machine(annotationKey: null));

        await Check(reach, MachineRequest("3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2d"), access);

        documents.Reads.ShouldBe([$"{Organization}/{Project}/3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2d"]);
    }

    // A bearer token does not name its project, so every reach checks the credential policy of the project.
    [Theory]
    [MemberData(nameof(EveryReach))]
    public async Task BearerMachine_ProjectWithoutClientCredentials_IsUnauthorized(Reach reach)
    {
        var store = new PolicyStore(MachineCredentialKind.ApiKey);
        var request = MachineRequest(services: PolicyServices(store), credentialKind: MachineCredentialKind.ClientCredentials);

        await Should.ThrowAsync<SecurityTokenException>(() => Check(reach, request, new RoleAccess(AccountRole.Reader)));

        store.Reads.ShouldBe([$"{Organization}/{Project}"]);
    }

    // The credential policy comes before the scopes: 401, not the 403 of a scope the machine was not created with and
    // its credentials do not carry.
    [Theory]
    [MemberData(nameof(EveryReach))]
    public async Task BearerMachine_ProjectWithoutClientCredentials_WithoutTheScope_IsUnauthorizedFirst(Reach reach)
    {
        var store = new PolicyStore(MachineCredentialKind.ApiKey);
        var request = TestRequests.Machine([MachineScope.AnnotationRead], services: PolicyServices(store), credentialKind: MachineCredentialKind.ClientCredentials);
        var access = new RoleAccess(AccountRole.None, scopes: [MachineScope.AnnotationRead]);

        await Should.ThrowAsync<SecurityTokenException>(() => Check(reach, request, access, scope: MachineScope.ConfigurationWrite));

        store.Reads.ShouldBe([$"{Organization}/{Project}"]);
    }

    [Fact]
    public async Task BearerMachine_ProjectWithClientCredentials_IsStillRestricted()
    {
        var store = new PolicyStore(MachineCredentialKind.ClientCredentials);
        var request = MachineRequest(services: PolicyServices(store), credentialKind: MachineCredentialKind.ClientCredentials);

        await request.CheckAccessToAnnotationAsync(Restricted(), Organization, Project, Descendant, AccountRole.Reader, MachineScope.ConfigurationRead);
        await ShouldBeRestricted(() => request.CheckAccessToProjectAsync(Restricted(), Organization, Project, AccountRole.Reader, MachineScope.ConfigurationRead));
        await ShouldBeRestricted(() => request.CheckAccessToAnnotationAsync(Restricted(), Organization, Project, "usg.S.R", AccountRole.Reader, MachineScope.ConfigurationRead));
    }

    // Each data helper names the machine scope of its operation: stored on the machine document and carried by the
    // credentials. Annotation.Write stands for any scope; the machine has it and nothing else.
    [Theory]
    [MemberData(nameof(EveryReach))]
    public async Task Machine_WithTheScopeStoredAndCarried_Passes(Reach reach)
    {
        var access = new RoleAccess(AccountRole.None, scopes: [MachineScope.AnnotationWrite]);

        await Check(reach, TestRequests.Machine([MachineScope.AnnotationWrite]), access, scope: MachineScope.AnnotationWrite);
    }

    // An app role or a provider permission added outside Storyteller grants nothing: the credentials carry every scope,
    // the machine was created to read.
    [Theory]
    [MemberData(nameof(EveryReach))]
    public async Task Machine_WithoutTheStoredScope_IsAccessDenied(Reach reach)
    {
        var access = new RoleAccess(AccountRole.None, scopes: [MachineScope.AnnotationRead, MachineScope.ConfigurationRead]);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => Check(reach, TestRequests.Machine(), access, scope: MachineScope.AnnotationWrite));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("This operation needs the scope Annotation.Write, which the machine was not created with.");
    }

    // The credentials narrow the machine: it has every runtime scope, its token asked for the reads only.
    [Theory]
    [MemberData(nameof(EveryReach))]
    public async Task Machine_WithoutTheCarriedScope_IsAccessDenied(Reach reach)
    {
        var request = TestRequests.Machine([MachineScope.AnnotationRead, MachineScope.ConfigurationRead]);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => Check(reach, request, new RoleAccess(AccountRole.None), scope: MachineScope.AnnotationWrite));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("This operation needs the scope Annotation.Write, which the credentials of the machine do not carry.");
    }

    // A scope string is compared as it is; only the App. prefix of an app role is dropped (GetScopes).
    [Theory]
    [InlineData("App.Annotation.Write", true)]
    [InlineData("annotation.write", false)]
    [InlineData("AnnotationWrite", false)]
    [InlineData("Annotation.ReadWrite", false)]
    public async Task Machine_CarriedScope_IsTheExactScopeString(string carried, bool passes)
    {
        var request = TestRequests.MachineCarrying([carried]);
        var check = () => request.CheckAccessToProjectAsync(new RoleAccess(AccountRole.None), Organization, Project, AccountRole.Reader, MachineScope.AnnotationWrite);

        if (passes)
        {
            await check();
        }
        else
        {
            var exception = await Should.ThrowAsync<AccessDeniedException>(check);
            exception.Message.ShouldBe("This operation needs the scope Annotation.Write, which the credentials of the machine do not carry.");
        }
    }

    // The restriction comes first: a restricted machine without the scope learns that the operation is out of its
    // reach. Inside of it, and for the project metadata, which every machine of the project reaches, the missing scope
    // is AccessDenied.
    [Fact]
    public async Task RestrictedMachine_WithoutTheScope_IsRestrictedFirst()
    {
        const MachineScope scope = MachineScope.ConfigurationWrite;
        var access = new RoleAccess(AccountRole.None, Restriction, scopes: [MachineScope.AnnotationRead]);
        var request = TestRequests.Machine([MachineScope.AnnotationRead]);

        await ShouldBeRestricted(() => request.CheckAccessToProjectAsync(access, Organization, Project, AccountRole.Reader, scope));
        await ShouldBeRestricted(() => request.CheckAccessToAnnotationAsync(access, Organization, Project, "usg.S.R", AccountRole.Reader, scope));
        await ShouldBeRestricted(() => request.CheckAccessToAnnotationsAsync(access, Organization, Project, [Restriction, "usg.S.R"], AccountRole.Reader, scope));

        var inside = await Should.ThrowAsync<AccessDeniedException>(() => request.CheckAccessToAnnotationAsync(access, Organization, Project, Descendant, AccountRole.Reader, scope));
        var insideBulk = await Should.ThrowAsync<AccessDeniedException>(() => request.CheckAccessToAnnotationsAsync(access, Organization, Project, [Restriction, Descendant], AccountRole.Reader, scope));
        var metadata = await Should.ThrowAsync<AccessDeniedException>(() => request.CheckAccessToProjectMetadataAsync(access, Organization, Project, AccountRole.Reader, scope));

        foreach (var exception in new[] { inside, insideBulk, metadata })
        {
            exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
            exception.Message.ShouldBe("This operation needs the scope Configuration.Write, which the machine was not created with.");
        }
    }

    // The project metadata (the views) needs any one of its scopes: a machine created for annotations or for
    // configurations reads it, and so does one whose credentials carry only one of the two.
    [Theory]
    [InlineData(new[] { MachineScope.AnnotationRead }, new[] { MachineScope.AnnotationRead })]
    [InlineData(new[] { MachineScope.ConfigurationRead }, new[] { MachineScope.ConfigurationRead })]
    [InlineData(new[] { MachineScope.AnnotationRead, MachineScope.ConfigurationRead }, new[] { MachineScope.ConfigurationRead })]
    [InlineData(new[] { MachineScope.AnnotationRead, MachineScope.ConfigurationRead }, new[] { MachineScope.AnnotationRead })]
    public async Task Metadata_WithEitherScope_Passes(MachineScope[] stored, MachineScope[] carried)
    {
        await CheckMetadata(TestRequests.Machine(carried), new RoleAccess(AccountRole.None, scopes: stored));
    }

    [Fact]
    public async Task Metadata_WithNeitherScopeStored_IsAccessDenied()
    {
        var access = new RoleAccess(AccountRole.None, scopes: [MachineScope.SecretsRead, MachineScope.ConfigurationTransfer]);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => CheckMetadata(TestRequests.Machine(), access));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("This operation needs one of the scopes Annotation.Read or Configuration.Read, which the machine was not created with.");
    }

    // Stored but not carried, or carried but not stored: neither scope is both.
    [Theory]
    [InlineData(new[] { MachineScope.AnnotationRead, MachineScope.ConfigurationRead }, new[] { MachineScope.AnnotationWrite, MachineScope.ConfigurationWrite })]
    [InlineData(new[] { MachineScope.AnnotationRead }, new[] { MachineScope.ConfigurationRead })]
    public async Task Metadata_WithNeitherScopeCarried_IsAccessDenied(MachineScope[] stored, MachineScope[] carried)
    {
        var exception = await Should.ThrowAsync<AccessDeniedException>(() => CheckMetadata(TestRequests.Machine(carried), new RoleAccess(AccountRole.None, scopes: stored)));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("This operation needs one of the scopes Annotation.Read or Configuration.Read, which the credentials of the machine do not carry.");
    }

    // A management operation names its runtime scope and its management scope (424-57): a machine needs both stored on
    // its document and carried by its credentials, on top of everything else, in every build.
    [Theory]
    [MemberData(nameof(EveryManagementReach))]
    public async Task Machine_WithTheManagementScopeStoredAndCarried_Passes(Reach reach)
    {
        var access = new RoleAccess(AccountRole.Reader, managementScopes: [MachineScope.ConfigurationTransfer, MachineScope.SchemaWrite]);

        await CheckManagement(reach, MachineRequest(managementScopes: [MachineScope.SchemaWrite]), access, MachineScope.SchemaWrite);
    }

    [Theory]
    [MemberData(nameof(EveryManagementReach))]
    public async Task Machine_WithoutTheStoredManagementScope_IsAccessDenied(Reach reach)
    {
        var access = new RoleAccess(AccountRole.Reader, managementScopes: [MachineScope.ConfigurationTransfer]);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => CheckManagement(reach, MachineRequest(managementScopes: [MachineScope.SchemaWrite]), access, MachineScope.SchemaWrite));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("This operation needs the scope Schema.Write, which the machine was not created with.");
    }

    // The token only narrows the machine document: a federated token that asked for a subset, or a provider token without
    // the permission or the app role.
    [Theory]
    [MemberData(nameof(EveryManagementReach))]
    public async Task Machine_WithoutTheCarriedManagementScope_IsAccessDenied(Reach reach)
    {
        var access = new RoleAccess(AccountRole.Reader, managementScopes: [MachineScope.ConfigurationTransfer, MachineScope.SchemaWrite]);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => CheckManagement(reach, MachineRequest(managementScopes: [MachineScope.ConfigurationTransfer]), access, MachineScope.SchemaWrite));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("This operation needs the scope Schema.Write, which the credentials of the machine do not carry.");
    }

    // The runtime scopes never stand in for a management scope.
    [Theory]
    [MemberData(nameof(EveryManagementReach))]
    public async Task Machine_WithTheRuntimeScopesOnly_IsAccessDenied(Reach reach)
    {
        var exception = await Should.ThrowAsync<AccessDeniedException>(() => CheckManagement(reach, MachineRequest(), new RoleAccess(AccountRole.Reader), MachineScope.ConfigurationTransfer));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("This operation needs the scope Configuration.Transfer, which the machine was not created with.");
    }

    // Nor a management scope for its runtime scope: the scopes are checked in the order given, the runtime scope first.
    [Theory]
    [MemberData(nameof(EveryManagementReach))]
    public async Task Machine_WithTheManagementScopeOnly_IsDeniedTheRuntimeScopeFirst(Reach reach)
    {
        var access = new RoleAccess(AccountRole.None, scopes: [MachineScope.AnnotationRead, MachineScope.SchemaWrite]);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => CheckManagement(reach, TestRequests.Machine(), access, MachineScope.SchemaWrite));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("This operation needs the scope Configuration.Read, which the machine was not created with.");
    }

    // Without both scopes the refusal names the runtime scope too: neither stored on the machine, or neither carried.
    [Theory]
    [MemberData(nameof(EveryManagementReach))]
    public async Task Machine_WithNeitherScopeStored_IsDeniedTheRuntimeScopeFirst(Reach reach)
    {
        var access = new RoleAccess(AccountRole.None, scopes: [MachineScope.AnnotationRead]);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => CheckManagement(reach, TestRequests.Machine(), access, MachineScope.SchemaWrite));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("This operation needs the scope Configuration.Read, which the machine was not created with.");
    }

    [Theory]
    [MemberData(nameof(EveryManagementReach))]
    public async Task Machine_WithNeitherScopeCarried_IsDeniedTheRuntimeScopeFirst(Reach reach)
    {
        var access = new RoleAccess(AccountRole.None, scopes: [MachineScope.AnnotationRead, MachineScope.ConfigurationRead, MachineScope.SchemaWrite]);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => CheckManagement(reach, TestRequests.Machine([MachineScope.AnnotationRead]), access, MachineScope.SchemaWrite));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("This operation needs the scope Configuration.Read, which the credentials of the machine do not carry.");
    }

    // The restriction comes first, so a restricted machine learns that the operation is out of its reach; inside of it,
    // the missing management scope is AccessDenied.
    [Fact]
    public async Task RestrictedMachine_WithoutTheManagementScope_IsRestrictedFirst()
    {
        const MachineScope scope = MachineScope.AnnotationManage;
        var access = Restricted();

        await ShouldBeRestricted(() => MachineRequest().CheckAccessToProjectAsync(access, Organization, Project, AccountRole.Reader, MachineScope.ConfigurationRead, scope));
        await ShouldBeRestricted(() => MachineRequest().CheckAccessToAnnotationAsync(access, Organization, Project, "usg.S.R", AccountRole.Reader, MachineScope.ConfigurationRead, scope));
        await ShouldBeRestricted(() => MachineRequest().CheckAccessToAnnotationsAsync(access, Organization, Project, [Restriction, "usg.S.R"], AccountRole.Reader, MachineScope.ConfigurationRead, scope));

        var inside = await Should.ThrowAsync<AccessDeniedException>(() => MachineRequest().CheckAccessToAnnotationAsync(access, Organization, Project, Descendant, AccountRole.Reader, MachineScope.ConfigurationRead, scope));
        var insideBulk = await Should.ThrowAsync<AccessDeniedException>(() => MachineRequest().CheckAccessToAnnotationsAsync(access, Organization, Project, [Restriction, Descendant], AccountRole.Reader, MachineScope.ConfigurationRead, scope));
        inside.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        insideBulk.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
    }

    [Fact]
    public async Task RestrictedMachine_WithTheManagementScope_PassesInsideItsRestriction()
    {
        var access = new RoleAccess(AccountRole.Reader, Restriction, [MachineScope.AnnotationManage]);
        var request = MachineRequest(managementScopes: [MachineScope.AnnotationManage]);

        await request.CheckAccessToAnnotationAsync(access, Organization, Project, Descendant, AccountRole.Reader, MachineScope.ConfigurationRead, MachineScope.AnnotationManage);
        await request.CheckAccessToAnnotationsAsync(access, Organization, Project, [Restriction, Descendant], AccountRole.Reader, MachineScope.ConfigurationRead, MachineScope.AnnotationManage);
    }

    // An unknown machine gets 401 before any 403.
    [Theory]
    [MemberData(nameof(EveryManagementReach))]
    public async Task MissingMachine_OfAManagementOperation_IsUnauthorized(Reach reach)
    {
        var (access, _) = MachineDocuments.Create(machine: null);

        await Should.ThrowAsync<SecurityTokenException>(() => CheckManagement(reach, MachineRequest(managementScopes: [MachineScope.SchemaWrite]), access, MachineScope.SchemaWrite));
    }

    // Users never need a management scope; their role decides, as before.
    [Theory]
    [MemberData(nameof(EveryManagementReach))]
    public async Task User_OfAManagementOperation_PassesWithoutAManagementScope(Reach reach)
    {
        await CheckManagement(reach, KeyValueRequests.User(), new RoleAccess(AccountRole.Administrator), MachineScope.ConfigurationTransfer, AccountRole.Administrator);
    }

    [Theory]
    [MemberData(nameof(EveryManagementReach))]
    public async Task User_OfAManagementOperation_BelowTheMinimalRole_IsStillDenied(Reach reach)
    {
        if (DevAuth)
        {
            return;
        }

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => CheckManagement(reach, KeyValueRequests.User(), new RoleAccess(AccountRole.Contributor), MachineScope.ConfigurationTransfer, AccountRole.Administrator));

        exception.Message.ShouldBe($"No Administrator access to '{Organization}.{Project}'.");
    }

    // Users: every entry point hands its minimal role to the role check of Release builds (403 AccessDenied below it).
    [Theory]
    [MemberData(nameof(UserBelowTheMinimalRole))]
    public async Task User_BelowTheMinimalRole_IsDenied(Reach reach, AccountRole role, AccountRole minimalRole)
    {
        if (DevAuth)
        {
            return;
        }

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => Check(reach, KeyValueRequests.User(), new RoleAccess(role), minimalRole));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
    }

    // A delegated token of a listed client app (424-84, the MCP server's on-behalf-of token) is classified as a user
    // by the Entra validator: no machine identity, its own azp and tid. It goes through the same role check.
    [Theory]
    [MemberData(nameof(EveryReach))]
    public async Task UserOfADelegatedClient_WithTheRole_Passes(Reach reach)
    {
        await Check(reach, DelegatedUser(), new RoleAccess(AccountRole.Contributor), AccountRole.Contributor, MachineScope.AnnotationWrite);
    }

    [Theory]
    [MemberData(nameof(EveryReach))]
    public async Task UserOfADelegatedClient_BelowTheRole_IsDenied(Reach reach)
    {
        if (DevAuth)
        {
            return;
        }

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => Check(reach, DelegatedUser(), new RoleAccess(AccountRole.Reader), AccountRole.Contributor));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
    }

    // The token carries only User.Impersonation: a user needs no data scope, the role decides.
    [Theory]
    [MemberData(nameof(UserWithTheMinimalRole))]
    public async Task User_WithTheMinimalRole_Passes(Reach reach, AccountRole role, AccountRole minimalRole)
    {
        await Check(reach, KeyValueRequests.User(), new RoleAccess(role), minimalRole, MachineScope.AnnotationWrite);
    }

    // A token that does not act for a user is 401 in Release builds, whatever data scopes it carries; Debug builds
    // (DEV_AUTH) skip the check and serve it.
    [Theory]
    [InlineData(Reach.Project)]
    [InlineData(Reach.AccessPoint)]
    [InlineData(Reach.Annotation)]
    [InlineData(Reach.Annotations)]
    [InlineData(Reach.Metadata)]
    public async Task User_WithoutUserImpersonation_IsUnauthorized(Reach reach)
    {
        var request = TestRequests.UserCarrying(MachineScopes.ToScopeStrings(MachineScopes.All));
        var check = () => Check(reach, request, new RoleAccess(AccountRole.Owner), scope: MachineScope.ConfigurationRead);

        if (DevAuth)
        {
            await check();
            return;
        }

        var exception = await Should.ThrowAsync<SecurityTokenException>(check);

        exception.Message.ShouldBe("The token does not act for a user: the scope User.Impersonation is missing.");
    }

    // Data scopes in a user token are ignored: they never stand in for the role.
    [Theory]
    [InlineData(Reach.Project)]
    [InlineData(Reach.AccessPoint)]
    [InlineData(Reach.Annotation)]
    [InlineData(Reach.Annotations)]
    [InlineData(Reach.Metadata)]
    public async Task User_WithDataScopesBelowTheMinimalRole_IsDenied(Reach reach)
    {
        if (DevAuth)
        {
            return;
        }

        var request = TestRequests.UserCarrying([Scopes.User.Impersonation, .. MachineScopes.ToScopeStrings(MachineScopes.All)]);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => Check(reach, request, new RoleAccess(AccountRole.Reader), AccountRole.Contributor));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe($"No Contributor access to '{Organization}.{Project}'.");
    }

    // The keys are inside Restriction, so only the project-wide reaches refuse a restricted machine. The data reaches need
    // the given machine scope, by default the runtime scope Configuration.Read, which MachineRequest carries; the access
    // point is a route for users only.
    private static Task Check(
        Reach reach,
        TestHttpRequestData request,
        IAccessService access,
        AccountRole minimalRole = AccountRole.Reader,
        MachineScope scope = MachineScope.ConfigurationRead)
    {
        return reach switch
        {
            Reach.Project => request.CheckAccessToProjectAsync(access, Organization, Project, minimalRole, scope),
            Reach.AccessPoint => request.CheckUserAccessToAsync(access, $"{Organization}.{Project}", minimalRole),
            Reach.Annotation => request.CheckAccessToAnnotationAsync(access, Organization, Project, Restriction, minimalRole, scope),
            Reach.Annotations => request.CheckAccessToAnnotationsAsync(access, Organization, Project, [Restriction, Descendant], minimalRole, scope),
            Reach.Metadata => request.CheckAccessToProjectMetadataAsync(access, Organization, Project, minimalRole, scope),
            _ => throw new ArgumentOutOfRangeException(nameof(reach), reach, null),
        };
    }

    // The project metadata as GetViews asks for it: either read scope.
    private static Task CheckMetadata(TestHttpRequestData request, IAccessService access)
    {
        return request.CheckAccessToProjectMetadataAsync(access, Organization, Project, AccountRole.Reader, MachineScope.AnnotationRead, MachineScope.ConfigurationRead);
    }

    // The entry points of a management operation: the whole project, one annotation and several, with the runtime scope
    // Configuration.Read first and then the management scope. The keys are inside Restriction.
    private static Task CheckManagement(
        Reach reach,
        TestHttpRequestData request,
        IAccessService access,
        MachineScope managementScope,
        AccountRole minimalRole = AccountRole.Reader)
    {
        return reach switch
        {
            Reach.Project => request.CheckAccessToProjectAsync(access, Organization, Project, minimalRole, MachineScope.ConfigurationRead, managementScope),
            Reach.Annotation => request.CheckAccessToAnnotationAsync(access, Organization, Project, Restriction, minimalRole, MachineScope.ConfigurationRead, managementScope),
            Reach.Annotations => request.CheckAccessToAnnotationsAsync(access, Organization, Project, [Restriction, Descendant], minimalRole, MachineScope.ConfigurationRead, managementScope),
            _ => throw new ArgumentOutOfRangeException(nameof(reach), reach, null),
        };
    }

    private static TheoryData<Reach, AccountRole, AccountRole> Users(params (AccountRole Role, AccountRole MinimalRole)[] roles)
    {
        var cases = new TheoryData<Reach, AccountRole, AccountRole>();

        foreach (var reach in Enum.GetValues<Reach>())
        {
            foreach (var (role, minimalRole) in roles)
            {
                cases.Add(reach, role, minimalRole);
            }
        }

        return cases;
    }

    private static async Task ShouldBeRestricted(Func<Task> check)
    {
        var exception = await Should.ThrowAsync<AccessDeniedException>(check);

        exception.ErrorCode.ShouldBe(ErrorCodes.AnnotationRestricted);
    }

    private static async Task ShouldBeForUsersOnly(Func<Task> check)
    {
        var exception = await Should.ThrowAsync<AccessDeniedException>(check);

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("Only users may call this operation; machines cannot.");
    }

    private static RoleAccess Restricted() => new(AccountRole.Reader, Restriction);

    // The claims of an on-behalf-of token of a listed client app, as the Entra validator leaves a user: no machine
    // identity, the azp and tid of the client next to sub and User.Impersonation.
    private static TestHttpRequestData DelegatedUser()
    {
        var request = TestRequests.User();
        var claims = (List<System.Security.Claims.Claim>)request.FunctionContext.Items[FunctionContextItemKeys.CachedClaims];
        claims.Add(new System.Security.Claims.Claim("azp", "6c0f1a2e-1f43-4b0e-9a4d-2f5e8f0c9b11"));
        claims.Add(new System.Security.Claims.Claim("tid", "8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d"));
        return request;
    }

    // The credentials carry the runtime scope Configuration.Read and the given management scopes as roles.
    private static TestHttpRequestData MachineRequest(
        string machineId = MachineId,
        IServiceProvider? services = null,
        MachineCredentialKind? credentialKind = null,
        IReadOnlyList<MachineScope>? managementScopes = null)
    {
        return TestRequests.Machine([MachineScope.ConfigurationRead, .. managementScopes ?? []], services: services, machineId: machineId, credentialKind: credentialKind);
    }

    private static ServiceProvider PolicyServices(IMachineAuthenticationPolicyStore store)
    {
        return new ServiceCollection()
            .AddSingleton(store)
            .AddSingleton(Options.Create(new MachineAuthenticationOptions()))
            .BuildServiceProvider();
    }

    private static MachineAccess Machine(string? annotationKey) => new()
    {
        Id = MachineId,
        ObjectId = MachineId,
        AccessKey = "***",
        Scopes = [MachineScope.ConfigurationRead],
        AnnotationKey = annotationKey,
    };

    // Answers GetMachineAccessAsync with the given machine and records what it was asked; any other call fails.
    public class MachineDocuments : DispatchProxy
    {
        public MachineAccess? Machine { get; private set; }

        public List<string> Reads { get; } = [];

        public static (IAccessService Access, MachineDocuments Documents) Create(MachineAccess? machine)
        {
            var access = DispatchProxy.Create<IAccessService, MachineDocuments>();
            var documents = (MachineDocuments)(object)access;
            documents.Machine = machine;
            return (access, documents);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IAccessService.GetMachineAccessAsync))
            {
                throw new InvalidOperationException($"{targetMethod?.Name} must not be called for a machine.");
            }

            Reads.Add(string.Join('/', args ?? []));
            return Task.FromResult(Machine);
        }
    }

    private sealed class PolicyStore(MachineCredentialKind kind) : IMachineAuthenticationPolicyStore
    {
        public List<string> Reads { get; } = [];

        public Task<MachineAuthenticationPolicy?> GetAsync(string organization, string project)
        {
            Reads.Add($"{organization}/{project}");
            return Task.FromResult<MachineAuthenticationPolicy?>(new MachineAuthenticationPolicy { CredentialKind = kind });
        }

        public Task SetAsync(string organization, string project, MachineAuthenticationPolicy policy)
        {
            throw new NotSupportedException();
        }
    }
}
