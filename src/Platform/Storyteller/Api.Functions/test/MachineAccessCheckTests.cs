using System.Reflection;
using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api;
using _42.Platform.Storyteller.Api.Security;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The access checks of machine requests: the machine document of the project (401 without it), the credential policy,
// what the operation reaches against the annotation restriction of the machine (403 AnnotationRestricted), and the
// management scope of a management operation (403 AccessDenied). The machine branch runs in every build, so these tests
// run in Debug (DEV_AUTH) too. The role check of users (the User_ tests) runs in Release only.
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

    public static TheoryData<Reach> EveryReach => new()
    {
        Reach.Project,
        Reach.AccessPoint,
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

    // Debug builds (DEV_AUTH) skip the role check of users.
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
        await MachineRequest().CheckAccessToProjectAsync(new RoleAccess(AccountRole.Reader, annotationKey), Organization, Project);
    }

    // Without a restriction the key is not looked at; the handler answers 400 for a key that does not parse.
    [Theory]
    [InlineData("rst.R")]
    [InlineData("not-a-key")]
    public async Task UnrestrictedMachine_PassesAnyAnnotationKey(string annotationKey)
    {
        var access = new RoleAccess(AccountRole.Reader);

        await MachineRequest().CheckAccessToAnnotationAsync(access, Organization, Project, annotationKey);
        await MachineRequest().CheckAccessToAnnotationsAsync(access, Organization, Project, [annotationKey, null]);
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
        await MachineRequest().CheckAccessToAnnotationAsync(Restricted(), Organization, Project, annotationKey);
    }

    [Theory]
    [InlineData("usg.S.R")]
    [InlineData("cnt.S.C")]
    [InlineData("rst.R")]
    [InlineData("sbt.S")]
    public async Task RestrictedMachine_Ancestor_IsRestricted(string annotationKey)
    {
        await ShouldBeRestricted(() => MachineRequest().CheckAccessToAnnotationAsync(Restricted(), Organization, Project, annotationKey));
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
        await ShouldBeRestricted(() => MachineRequest().CheckAccessToAnnotationAsync(Restricted(), Organization, Project, annotationKey));
    }

    [Theory]
    [InlineData("exe.S.R")]
    [InlineData("xyz.S.R.C")]
    [InlineData("not-a-key")]
    [InlineData("")]
    public async Task RestrictedMachine_KeyThatDoesNotParse_IsRestricted(string annotationKey)
    {
        await ShouldBeRestricted(() => MachineRequest().CheckAccessToAnnotationAsync(Restricted(), Organization, Project, annotationKey));
    }

    [Fact]
    public async Task RestrictedMachine_ProjectCheck_IsRestricted()
    {
        await ShouldBeRestricted(() => MachineRequest().CheckAccessToProjectAsync(Restricted(), Organization, Project));
    }

    [Fact]
    public async Task RestrictedMachine_AccessPointOfTheProject_IsRestricted()
    {
        await ShouldBeRestricted(() => MachineRequest().CheckAccessToAsync(Restricted(), $"{Organization}.{Project}"));
    }

    [Fact]
    public async Task RestrictedMachine_BulkInsideItsRestriction_Passes()
    {
        await MachineRequest().CheckAccessToAnnotationsAsync(Restricted(), Organization, Project, [Restriction, Descendant, "UXE.S.R.C.V"]);
    }

    [Theory]
    [InlineData("usg.S.R")]
    [InlineData("exe.S.R.D")]
    [InlineData("not-a-key")]
    [InlineData(null)]
    public async Task RestrictedMachine_BulkWithOneKeyOutside_IsRestricted(string? outside)
    {
        await ShouldBeRestricted(() => MachineRequest().CheckAccessToAnnotationsAsync(Restricted(), Organization, Project, [Restriction, outside, Descendant]));
    }

    [Fact]
    public async Task RestrictedMachine_PassesTheProjectMetadata()
    {
        await MachineRequest().CheckAccessToProjectMetadataAsync(Restricted(), Organization, Project);
    }

    // Machines created before the key was normalized keep the key as it was sent.
    [Fact]
    public async Task RestrictedMachine_StoredKeyInAnotherForm_IsNormalized()
    {
        var access = new RoleAccess(AccountRole.Reader, "EXE..S.R.C");

        await MachineRequest().CheckAccessToAnnotationAsync(access, Organization, Project, Restriction);
        await MachineRequest().CheckAccessToAnnotationAsync(access, Organization, Project, Descendant);
        await ShouldBeRestricted(() => MachineRequest().CheckAccessToAnnotationAsync(access, Organization, Project, "usg.S.R"));
    }

    // A stored key that does not parse restricts the machine to nothing, not even its own text.
    [Fact]
    public async Task MachineWithAKeyThatDoesNotParse_ReachesNoAnnotation()
    {
        var access = new RoleAccess(AccountRole.Reader, "exe.S.R");

        await ShouldBeRestricted(() => MachineRequest().CheckAccessToAnnotationAsync(access, Organization, Project, "exe.S.R"));
        await ShouldBeRestricted(() => MachineRequest().CheckAccessToAnnotationsAsync(access, Organization, Project, [Restriction]));
        await ShouldBeRestricted(() => MachineRequest().CheckAccessToProjectAsync(access, Organization, Project));
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

    [Theory]
    [InlineData(null)]
    [InlineData(Restriction)]
    public async Task Machine_OrganizationAccessPoint_IsUnauthorized(string? annotationKey)
    {
        var (access, documents) = MachineDocuments.Create(Machine(annotationKey));

        await Should.ThrowAsync<SecurityTokenException>(() => MachineRequest().CheckAccessToAsync(access, Organization));
        await Should.ThrowAsync<SecurityTokenException>(() => MachineRequest().CheckAccessToOrganizationAsync(access, Organization));

        documents.Reads.ShouldBeEmpty();
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

    [Fact]
    public async Task BearerMachine_ProjectWithClientCredentials_IsStillRestricted()
    {
        var store = new PolicyStore(MachineCredentialKind.ClientCredentials);
        var request = MachineRequest(services: PolicyServices(store), credentialKind: MachineCredentialKind.ClientCredentials);

        await request.CheckAccessToAnnotationAsync(Restricted(), Organization, Project, Descendant);
        await ShouldBeRestricted(() => request.CheckAccessToProjectAsync(Restricted(), Organization, Project));
        await ShouldBeRestricted(() => request.CheckAccessToAnnotationAsync(Restricted(), Organization, Project, "usg.S.R"));
    }

    // A management operation names its management scope (424-57): a machine needs it stored on its document and carried
    // by its credentials, on top of everything else, in every build.
    [Theory]
    [MemberData(nameof(EveryManagementReach))]
    public async Task Machine_WithTheManagementScopeStoredAndCarried_Passes(Reach reach)
    {
        var access = new RoleAccess(AccountRole.Reader, managementScopes: [MachineManagementScope.ConfigurationTransfer, MachineManagementScope.SchemaWrite]);

        await CheckManagement(reach, MachineRequest(managementScopes: [MachineManagementScope.SchemaWrite]), access, MachineManagementScope.SchemaWrite);
    }

    [Theory]
    [MemberData(nameof(EveryManagementReach))]
    public async Task Machine_WithoutTheStoredManagementScope_IsAccessDenied(Reach reach)
    {
        var access = new RoleAccess(AccountRole.Reader, managementScopes: [MachineManagementScope.ConfigurationTransfer]);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => CheckManagement(reach, MachineRequest(managementScopes: [MachineManagementScope.SchemaWrite]), access, MachineManagementScope.SchemaWrite));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("This operation needs the management scope Schema.Write, which the machine was not created with.");
    }

    // The token only narrows the machine document: a federated token that asked for a subset, or a provider token without
    // the permission or the app role.
    [Theory]
    [MemberData(nameof(EveryManagementReach))]
    public async Task Machine_WithoutTheCarriedManagementScope_IsAccessDenied(Reach reach)
    {
        var access = new RoleAccess(AccountRole.Reader, managementScopes: [MachineManagementScope.ConfigurationTransfer, MachineManagementScope.SchemaWrite]);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => CheckManagement(reach, MachineRequest(managementScopes: [MachineManagementScope.ConfigurationTransfer]), access, MachineManagementScope.SchemaWrite));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("This operation needs the management scope Schema.Write, which the credentials of the machine do not carry.");
    }

    // The runtime scopes of a preset never stand in for a management scope.
    [Theory]
    [MemberData(nameof(EveryManagementReach))]
    public async Task Machine_WithTheRuntimeScopesOnly_IsAccessDenied(Reach reach)
    {
        var exception = await Should.ThrowAsync<AccessDeniedException>(() => CheckManagement(reach, MachineRequest(), new RoleAccess(AccountRole.Reader), MachineManagementScope.ConfigurationTransfer));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("This operation needs the management scope Configuration.Transfer, which the machine was not created with.");
    }

    // The restriction comes first, so a restricted machine learns that the operation is out of its reach; inside of it,
    // the missing management scope is AccessDenied.
    [Fact]
    public async Task RestrictedMachine_WithoutTheManagementScope_IsRestrictedFirst()
    {
        const MachineManagementScope scope = MachineManagementScope.AnnotationManage;
        var access = Restricted();

        await ShouldBeRestricted(() => MachineRequest().CheckAccessToProjectAsync(access, Organization, Project, managementScope: scope));
        await ShouldBeRestricted(() => MachineRequest().CheckAccessToAnnotationAsync(access, Organization, Project, "usg.S.R", managementScope: scope));
        await ShouldBeRestricted(() => MachineRequest().CheckAccessToAnnotationsAsync(access, Organization, Project, [Restriction, "usg.S.R"], managementScope: scope));

        var inside = await Should.ThrowAsync<AccessDeniedException>(() => MachineRequest().CheckAccessToAnnotationAsync(access, Organization, Project, Descendant, managementScope: scope));
        var insideBulk = await Should.ThrowAsync<AccessDeniedException>(() => MachineRequest().CheckAccessToAnnotationsAsync(access, Organization, Project, [Restriction, Descendant], managementScope: scope));
        inside.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        insideBulk.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
    }

    [Fact]
    public async Task RestrictedMachine_WithTheManagementScope_PassesInsideItsRestriction()
    {
        var access = new RoleAccess(AccountRole.Reader, Restriction, [MachineManagementScope.AnnotationManage]);
        var request = MachineRequest(managementScopes: [MachineManagementScope.AnnotationManage]);

        await request.CheckAccessToAnnotationAsync(access, Organization, Project, Descendant, managementScope: MachineManagementScope.AnnotationManage);
        await request.CheckAccessToAnnotationsAsync(access, Organization, Project, [Restriction, Descendant], managementScope: MachineManagementScope.AnnotationManage);
    }

    // An unknown machine gets 401 before any 403.
    [Theory]
    [MemberData(nameof(EveryManagementReach))]
    public async Task MissingMachine_OfAManagementOperation_IsUnauthorized(Reach reach)
    {
        var (access, _) = MachineDocuments.Create(machine: null);

        await Should.ThrowAsync<SecurityTokenException>(() => CheckManagement(reach, MachineRequest(managementScopes: [MachineManagementScope.SchemaWrite]), access, MachineManagementScope.SchemaWrite));
    }

    // Users never need a management scope; their role decides, as before.
    [Theory]
    [MemberData(nameof(EveryManagementReach))]
    public async Task User_OfAManagementOperation_PassesWithoutAManagementScope(Reach reach)
    {
        await CheckManagement(reach, KeyValueRequests.User(), new RoleAccess(AccountRole.Administrator), MachineManagementScope.ConfigurationTransfer, AccountRole.Administrator);
    }

    [Theory]
    [MemberData(nameof(EveryManagementReach))]
    public async Task User_OfAManagementOperation_BelowTheMinimalRole_IsStillDenied(Reach reach)
    {
        if (DevAuth)
        {
            return;
        }

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => CheckManagement(reach, KeyValueRequests.User(), new RoleAccess(AccountRole.Contributor), MachineManagementScope.ConfigurationTransfer, AccountRole.Administrator));

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

    [Theory]
    [MemberData(nameof(UserWithTheMinimalRole))]
    public async Task User_WithTheMinimalRole_Passes(Reach reach, AccountRole role, AccountRole minimalRole)
    {
        await Check(reach, KeyValueRequests.User(), new RoleAccess(role), minimalRole);
    }

    // The keys are inside Restriction, so only the project-wide reaches refuse a restricted machine.
    private static Task Check(Reach reach, TestHttpRequestData request, IAccessService access, AccountRole minimalRole = AccountRole.Reader)
    {
        return reach switch
        {
            Reach.Project => request.CheckAccessToProjectAsync(access, Organization, Project, minimalRole),
            Reach.AccessPoint => request.CheckAccessToAsync(access, $"{Organization}.{Project}", minimalRole),
            Reach.Annotation => request.CheckAccessToAnnotationAsync(access, Organization, Project, Restriction, minimalRole),
            Reach.Annotations => request.CheckAccessToAnnotationsAsync(access, Organization, Project, [Restriction, Descendant], minimalRole),
            Reach.Metadata => request.CheckAccessToProjectMetadataAsync(access, Organization, Project, minimalRole),
            _ => throw new ArgumentOutOfRangeException(nameof(reach), reach, null),
        };
    }

    // The entry points that take a management scope: the whole project, one annotation and several. The keys are inside
    // Restriction.
    private static Task CheckManagement(
        Reach reach,
        TestHttpRequestData request,
        IAccessService access,
        MachineManagementScope managementScope,
        AccountRole minimalRole = AccountRole.Reader)
    {
        return reach switch
        {
            Reach.Project => request.CheckAccessToProjectAsync(access, Organization, Project, minimalRole, managementScope),
            Reach.Annotation => request.CheckAccessToAnnotationAsync(access, Organization, Project, Restriction, minimalRole, managementScope),
            Reach.Annotations => request.CheckAccessToAnnotationsAsync(access, Organization, Project, [Restriction, Descendant], minimalRole, managementScope),
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

    private static RoleAccess Restricted() => new(AccountRole.Reader, Restriction);

    // The credentials carry the runtime scope Configuration.Read and the given management scopes as roles.
    private static TestHttpRequestData MachineRequest(
        string machineId = MachineId,
        IServiceProvider? services = null,
        MachineCredentialKind? credentialKind = null,
        IReadOnlyList<MachineManagementScope>? managementScopes = null)
    {
        var (context, request) = FunctionTestDoubles.CreateRequest(services ?? new ServiceCollection().BuildServiceProvider());
        var claims = new List<Claim> { new("sub", machineId), new("roles", Scopes.Configuration.Read) };
        claims.AddRange((managementScopes ?? []).Select(scope => new Claim("roles", MachineAccessScopes.Get(scope)!)));
        context.Items[FunctionContextItemKeys.CachedClaims] = claims;
        context.Items[FunctionContextItemKeys.MachineIdentity] = machineId;

        if (credentialKind is not null)
        {
            context.Items[FunctionContextItemKeys.MachineCredentialKind] = credentialKind.Value;
        }

        return request;
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
        Scope = MachineAccessScope.ConfigurationRead,
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
