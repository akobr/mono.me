using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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

    [Fact]
    public async Task GetMachineAuthentication_WithoutAStoredPolicy_ReturnsTheDeploymentDefaults()
    {
        var http = CreateHttp(new RecordingAccess(Machine()), new PolicyStore());

        var result = await http.GetMachineAuthentication(ContributorRequest(), "Org1.Proj1");

        var policy = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<MachineAuthenticationPolicy>();
        policy.EnabledCredentialKinds.ShouldBe([MachineCredentialKind.Certificate]);
        policy.CredentialKind.ShouldBe(MachineCredentialKind.Certificate);
        policy.CertificateLifetimeDays.ShouldBe(180);
    }

    [Fact]
    public async Task GetMachineAuthentication_LegacyPolicy_IsReturnedWithItsOneKindEnabled()
    {
        var store = new PolicyStore { Stored = new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.ApiKey, CertificateLifetimeDays = 30 } };
        var http = CreateHttp(new RecordingAccess(Machine()), store);

        var result = await http.GetMachineAuthentication(ContributorRequest(), "org1.proj1");

        var policy = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<MachineAuthenticationPolicy>();
        policy.EnabledCredentialKinds.ShouldBe([MachineCredentialKind.ApiKey]);
        policy.CertificateLifetimeDays.ShouldBe(30);
        store.Reads.ShouldBe(["org1.proj1"]);
    }

    [Fact]
    public async Task PutMachineAuthentication_StoresTheNormalizedPolicyAndReturnsTheEffectiveOne()
    {
        var store = new PolicyStore();
        var http = CreateHttp(new RecordingAccess(Machine()), store);
        var requested = new MachineAuthenticationPolicy
        {
            EnabledCredentialKinds = [MachineCredentialKind.ClientCredentials, MachineCredentialKind.ApiKey],
        };

        var result = await http.PutMachineAuthentication(ContributorRequest(FunctionTestDoubles.ToJsonBody(requested)), "org1.proj1");

        store.Stored.ShouldNotBeNull();
        store.Stored.EnabledCredentialKinds.ShouldBe([MachineCredentialKind.ApiKey, MachineCredentialKind.ClientCredentials]);
        store.Stored.CredentialKind.ShouldBe(MachineCredentialKind.ClientCredentials);
        store.Stored.CertificateLifetimeDays.ShouldBeNull();
        var policy = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<MachineAuthenticationPolicy>();
        policy.CertificateLifetimeDays.ShouldBe(180);
    }

    [Fact]
    public async Task PutMachineAuthentication_InvalidPolicy_IsBadRequestAndNotStored()
    {
        var store = new PolicyStore();
        var http = CreateHttp(new RecordingAccess(Machine()), store);
        var requested = new MachineAuthenticationPolicy
        {
            EnabledCredentialKinds = [MachineCredentialKind.Certificate],
            CredentialKind = MachineCredentialKind.ApiKey,
        };

        var result = await http.PutMachineAuthentication(ContributorRequest(FunctionTestDoubles.ToJsonBody(requested)), "org1.proj1");

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<Models.ErrorResponse>()
            .Message.ShouldBe("The default credential kind ApiKey is not one of the enabled kinds.");
        store.Stored.ShouldBeNull();
    }

    [Fact]
    public async Task MachineAuthentication_OfAnOrganization_IsBadRequest()
    {
        var http = CreateHttp(new RecordingAccess(Machine()), new PolicyStore());
        var policy = new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.ApiKey };

        (await http.GetMachineAuthentication(ContributorRequest(), "org1")).ShouldBeOfType<BadRequestObjectResult>();
        (await http.PutMachineAuthentication(ContributorRequest(FunctionTestDoubles.ToJsonBody(policy)), "org1")).ShouldBeOfType<BadRequestObjectResult>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PostMachines_BlankAnnotationKey_CreatesAMachineOfTheWholeProject(string? annotationKey)
    {
        var access = new RecordingAccess(Machine());
        var http = CreateHttp(access);

        var result = await http.PostMachines(ContributorRequest(FunctionTestDoubles.ToJsonBody(MachineCreate(annotationKey))), "org1", "proj1");

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<MachineAccess>().AnnotationKey.ShouldBeNull();
        access.Created.ShouldNotBeNull();
        access.Created.AnnotationKey.ShouldBeNull();
        access.Created.Organization.ShouldBe("org1");
        access.Created.Project.ShouldBe("proj1");
    }

    [Theory]
    [InlineData("EXE..northwind.invoicing.prod", "exe.northwind.invoicing.prod")]
    [InlineData("Rst.Billing", "rst.Billing")]
    [InlineData("uxe.Northwind.Invoicing.Prod..Worker", "uxe.Northwind.Invoicing.Prod.Worker")]
    public async Task PostMachines_AnnotationKey_IsPassedInItsCanonicalForm(string annotationKey, string expected)
    {
        var access = new RecordingAccess(Machine());
        var http = CreateHttp(access);

        var result = await http.PostMachines(ContributorRequest(FunctionTestDoubles.ToJsonBody(MachineCreate(annotationKey))), "org1", "proj1");

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<MachineAccess>().AnnotationKey.ShouldBe(expected);
        access.Created.ShouldNotBeNull();
        access.Created.AnnotationKey.ShouldBe(expected);
        access.Created.Scope.ShouldBe(MachineAccessScope.ConfigurationRead);
    }

    [Theory]
    [InlineData("rst.Billing")]
    [InlineData("sbt.Northwind")]
    [InlineData("unt.Billing.Invoices")]
    [InlineData("usg.Northwind.Billing")]
    [InlineData("cnt.Northwind.Prod")]
    [InlineData("exe.northwind.invoicing.prod")]
    [InlineData("uxe.Northwind.Invoicing.Prod.Worker")]
    public async Task PostMachines_CanonicalAnnotationKey_IsPassedUnchanged(string annotationKey)
    {
        var access = new RecordingAccess(Machine());
        var http = CreateHttp(access);

        var result = await http.PostMachines(ContributorRequest(FunctionTestDoubles.ToJsonBody(MachineCreate(annotationKey))), "org1", "proj1");

        result.ShouldBeOfType<OkObjectResult>();
        access.Created.ShouldNotBeNull();
        access.Created.AnnotationKey.ShouldBe(annotationKey);
    }

    // A wrong segment count ('sbt.a.b') or an unknown type code is refused before the service is asked.
    [Theory]
    [InlineData("my-annotation-key")]
    [InlineData("sbt.a.b")]
    [InlineData("xyz.a")]
    public async Task PostMachines_InvalidAnnotationKey_IsBadRequestAndNoMachineIsCreated(string annotationKey)
    {
        var access = new RecordingAccess(Machine());
        var http = CreateHttp(access);

        var result = await http.PostMachines(ContributorRequest(FunctionTestDoubles.ToJsonBody(MachineCreate(annotationKey))), "org1", "proj1");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<Models.ErrorResponse>();
        error.ErrorCode.ShouldBe(ErrorCodes.InvalidKey);
        error.Message.ShouldContain(annotationKey);
        access.Created.ShouldBeNull();
    }

    // The worker's [FromBody] binding failed these bodies with a 500 before the handler ran.
    [Theory]
    [InlineData("", "The machine is missing.")]
    [InlineData("null", "The machine is missing.")]
    [InlineData("[1, 2]", "The request body is not a machine (at $).")]
    public async Task PostMachines_MissingOrInvalidBody_IsBadRequestAndNoMachineIsCreated(string body, string message)
    {
        var access = new RecordingAccess(Machine());
        var http = CreateHttp(access);

        var result = await http.PostMachines(ContributorRequest(body), "org1", "proj1");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<Models.ErrorResponse>();
        error.Message.ShouldBe(message);
        error.ErrorCode.ShouldBeNull();
        access.Created.ShouldBeNull();
    }

    [Fact]
    public async Task PostMachines_PropertyAndEnumNamesInAnyCase_AreRead()
    {
        var access = new RecordingAccess(Machine());
        var http = CreateHttp(access);
        const string body = """{ "organization": "", "project": "", "scope": "configurationread" }""";

        var result = await http.PostMachines(ContributorRequest(body), "org1", "proj1");

        result.ShouldBeOfType<OkObjectResult>();
        access.Created.ShouldNotBeNull();
        access.Created.Scope.ShouldBe(MachineAccessScope.ConfigurationRead);
    }

    [Theory]
    [InlineData("", "The machine authentication policy is missing.")]
    [InlineData("null", "The machine authentication policy is missing.")]
    [InlineData("[1, 2]", "The request body is not a machine authentication policy (at $).")]
    public async Task PutMachineAuthentication_MissingOrInvalidBody_IsBadRequestAndNotStored(string body, string message)
    {
        var store = new PolicyStore();
        var http = CreateHttp(new RecordingAccess(Machine()), store);

        var result = await http.PutMachineAuthentication(ContributorRequest(body), "org1.proj1");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<Models.ErrorResponse>();
        error.Message.ShouldBe(message);
        error.ErrorCode.ShouldBeNull();
        store.Stored.ShouldBeNull();
    }

    private static AccessHttp CreateHttp(RecordingAccess access, IMachineAuthenticationPolicyStore? policyStore = null)
    {
        var options = Options.Create(new MachineAuthenticationOptions
        {
            DefaultCredentialKind = MachineCredentialKind.Certificate,
            CertificateLifetimeDays = 180,
        });

        return new AccessHttp(access, FunctionTestDoubles.ApiJsonOptions, NullLogger<AccessHttp>.Instance, policyStore: policyStore, machineOptions: options);
    }

    // A contributor's request; a body goes as JSON, with its content type.
    private static TestHttpRequestData ContributorRequest(string? body = null)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var headers = body is null ? null : FunctionTestDoubles.JsonContentType();
        var (context, request) = FunctionTestDoubles.CreateRequest(services, headers, body: body);
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

    // The route decides the organization and project; the body ones are left empty.
    private static MachineAccessCreate MachineCreate(string? annotationKey)
    {
        return new MachineAccessCreate
        {
            Organization = string.Empty,
            Project = string.Empty,
            Scope = MachineAccessScope.ConfigurationRead,
            AnnotationKey = annotationKey,
        };
    }

    private sealed class PolicyStore : IMachineAuthenticationPolicyStore
    {
        public MachineAuthenticationPolicy? Stored { get; set; }

        public List<string> Reads { get; } = [];

        public Task<MachineAuthenticationPolicy?> GetAsync(string organization, string project)
        {
            Reads.Add($"{organization}.{project}");
            return Task.FromResult(Stored);
        }

        public Task SetAsync(string organization, string project, MachineAuthenticationPolicy policy)
        {
            Stored = policy;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAccess(MachineAccess machine) : IAccessService
    {
        public List<string> Calls { get; } = [];

        // The model the handler passed to CreateMachineAccessAsync; null while no machine was created.
        public MachineAccessCreate? Created { get; private set; }

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

        public Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model)
        {
            Calls.Add($"create:{model.Organization}/{model.Project}");
            Created = model;
            return Task.FromResult(machine with { Scope = model.Scope, AnnotationKey = model.AnnotationKey });
        }

        public Task<bool> VerifyAccessForMachineAsync(string organization, string project, string appId) => throw new NotImplementedException();

        public Task<IReadOnlyList<AccessPointMember>> GetMembersAsync(string accessPointKey, string actorId) => throw new NotImplementedException();

        public Task<AccessPointMember> SetMemberRoleAsync(string accessPointKey, string accountId, AccountRole role, string actorId) => throw new NotImplementedException();

        public Task RemoveMemberAsync(string accessPointKey, string accountId, string actorId) => throw new NotImplementedException();

        public Task<AccountRole> JoinAccessPointAsync(string accessPointKey, string accountId, AccountRole role) => throw new NotImplementedException();
    }
}
