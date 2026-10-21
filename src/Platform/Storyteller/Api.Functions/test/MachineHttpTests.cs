using System.Security.Claims;
using System.Text.Json;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class MachineHttpTests
{
    private const string KeycloakId = "42.sform.org1.proj1.0123456789abcdef0123456789abcdef";

    private const string NoRuntimeScope = "A machine needs at least one runtime scope: AnnotationRead, AnnotationWrite, ConfigurationRead, ConfigurationWrite, SecretsRead or SecretsWrite.";

    // The scopes of a new machine and the role they need (spec 4), each with every role of a user.
    public static TheoryData<MachineScope[], AccountRole, AccountRole> RequestedScopes => WithEveryRole(
        ([MachineScope.AnnotationRead], AccountRole.Contributor),
        ([MachineScope.AnnotationWrite, MachineScope.ConfigurationWrite], AccountRole.Contributor),
        ([MachineScope.ConfigurationRead, MachineScope.SecretsRead], AccountRole.ContributorWithSecrets),
        ([MachineScope.SecretsWrite], AccountRole.ContributorWithSecrets),
        ([MachineScope.ConfigurationRead, MachineScope.ConfigurationTransfer], AccountRole.Administrator),
        ([MachineScope.ConfigurationWrite, MachineScope.SchemaWrite], AccountRole.Administrator),
        ([MachineScope.ConfigurationWrite, MachineScope.TemplateWrite], AccountRole.Administrator),
        ([MachineScope.AnnotationWrite, MachineScope.AnnotationManage], AccountRole.Administrator),
        ([MachineScope.ConfigurationWrite, MachineScope.SecretsWrite, MachineScope.KeyValueManage], AccountRole.Administrator));

    // The scopes of a stored machine and the role its reset needs, each with every role of a user.
    public static TheoryData<MachineScope[], AccountRole, AccountRole> StoredScopes => WithEveryRole(
        ([MachineScope.AnnotationRead, MachineScope.ConfigurationRead], AccountRole.Contributor),
        ([MachineScope.AnnotationRead, MachineScope.AnnotationWrite, MachineScope.ConfigurationRead, MachineScope.ConfigurationWrite], AccountRole.Contributor),
        ([MachineScope.ConfigurationRead, MachineScope.SecretsRead], AccountRole.ContributorWithSecrets),
        ([MachineScope.SecretsRead, MachineScope.SecretsWrite], AccountRole.ContributorWithSecrets),
        ([MachineScope.AnnotationRead, MachineScope.ConfigurationRead, MachineScope.ConfigurationTransfer], AccountRole.Administrator),
        ([MachineScope.ConfigurationRead, MachineScope.ConfigurationWrite, MachineScope.SecretsRead, MachineScope.KeyValueManage], AccountRole.Administrator),
        ([.. MachineScopes.All], AccountRole.Administrator));

    private static bool DevAuth => HttpRequestDataExtensions.IsDevelopmentAuthentication;

    // The role checks of users look up one role per check; Debug builds (DEV_AUTH) skip them.
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
        access.Roles.ShouldBe(RoleLookups("org1.proj1"));
    }

    // The reset reads the stored machine once for its role rule; its runtime scopes need no more than the Contributor
    // of the route, so no second role is looked up.
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
                $"get:org1/proj1/{KeycloakId}",
                $"reset:org1/proj1/{KeycloakId}",
                $"delete:org1/proj1/{KeycloakId}",
            ]);
        access.Roles.ShouldBe(RoleLookups("org1.proj1", "org1.proj1", "org1.proj1"));
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

    // Setting the policy is for Administrators.
    [Fact]
    public async Task PutMachineAuthentication_StoresTheNormalizedPolicyAndReturnsTheEffectiveOne()
    {
        var store = new PolicyStore();
        var http = CreateHttp(Administrator(), store);
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
        var http = CreateHttp(Administrator(), store);
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

    // A user role check, so Debug builds (DEV_AUTH) skip it.
    [Fact]
    public async Task PutMachineAuthentication_ByAContributor_IsAccessDeniedAndNotStored()
    {
        if (DevAuth)
        {
            return;
        }

        var store = new PolicyStore();
        var http = CreateHttp(new RecordingAccess(Machine()), store);
        var policy = new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.ApiKey };

        var exception = await Should.ThrowAsync<AccessDeniedException>(
            () => http.PutMachineAuthentication(ContributorRequest(FunctionTestDoubles.ToJsonBody(policy)), "org1.proj1"));

        exception.Message.ShouldBe("No Administrator access to 'org1.proj1'.");
        store.Stored.ShouldBeNull();
    }

    [Fact]
    public async Task MachineAuthentication_OfAnOrganization_IsBadRequest()
    {
        var http = CreateHttp(Administrator(), new PolicyStore());
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
        access.Created.Scopes.ShouldBe([MachineScope.ConfigurationRead]);
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

    // The worker's [FromBody] binding failed these bodies with a 500 before the handler ran. The API reads enum names:
    // the scope strings and the kebab case that only sform accepts, and the names of the removed presets, are no
    // machine scopes, so such a body is not a machine either.
    [Theory]
    [InlineData("", "The machine is missing.")]
    [InlineData("null", "The machine is missing.")]
    [InlineData("[1, 2]", "The request body is not a machine (at $).")]
    [InlineData("""{ "Organization": "", "Project": "", "Scopes": ["Bogus"] }""", "The request body is not a machine (at $.Scopes[0]).")]
    [InlineData("""{ "Organization": "", "Project": "", "Scopes": ["Annotation.Read"] }""", "The request body is not a machine (at $.Scopes[0]).")]
    [InlineData("""{ "Organization": "", "Project": "", "Scopes": ["Configuration.Transfer"] }""", "The request body is not a machine (at $.Scopes[0]).")]
    [InlineData("""{ "Organization": "", "Project": "", "Scopes": ["annotation-read"] }""", "The request body is not a machine (at $.Scopes[0]).")]
    [InlineData("""{ "Organization": "", "Project": "", "Scopes": ["DefaultRead"] }""", "The request body is not a machine (at $.Scopes[0]).")]
    [InlineData("""{ "Organization": "", "Project": "", "Scopes": ["ConfigurationRead", "SecretsReadWrite"] }""", "The request body is not a machine (at $.Scopes[1]).")]
    [InlineData("""{ "Organization": "", "Project": "", "Scopes": "AnnotationRead" }""", "The request body is not a machine (at $.Scopes).")]
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
        const string body = """{ "organization": "", "project": "", "scopes": ["configurationread"] }""";

        var result = await http.PostMachines(ContributorRequest(body), "org1", "proj1");

        result.ShouldBeOfType<OkObjectResult>();
        access.Created.ShouldNotBeNull();
        access.Created.Scopes.ShouldBe([MachineScope.ConfigurationRead]);
    }

    // The stored form, passed to the service and returned: the defined scopes, distinct, in enum order, completed once
    // with X.Read for every X.Write. Secrets imply no Configuration scope and management scopes imply nothing. The JSON
    // reads integers as enum values too. An Owner may give every scope, so the role rule does not get in the way.
    [Theory]
    [InlineData("""["AnnotationWrite"]""", new[] { MachineScope.AnnotationRead, MachineScope.AnnotationWrite })]
    [InlineData("""["ConfigurationWrite"]""", new[] { MachineScope.ConfigurationRead, MachineScope.ConfigurationWrite })]
    [InlineData("""["SecretsWrite"]""", new[] { MachineScope.SecretsRead, MachineScope.SecretsWrite })]
    [InlineData("""["SecretsRead", "ConfigurationWrite"]""", new[] { MachineScope.ConfigurationRead, MachineScope.ConfigurationWrite, MachineScope.SecretsRead })]
    [InlineData("""["ConfigurationTransfer", "AnnotationRead"]""", new[] { MachineScope.AnnotationRead, MachineScope.ConfigurationTransfer })]
    [InlineData("""["SchemaWrite", "TemplateWrite", "ConfigurationWrite"]""", new[] { MachineScope.ConfigurationRead, MachineScope.ConfigurationWrite, MachineScope.SchemaWrite, MachineScope.TemplateWrite })]
    [InlineData("""["KeyValueManage", "ConfigurationWrite", "AnnotationRead", "KeyValueManage", "ConfigurationWrite"]""", new[] { MachineScope.AnnotationRead, MachineScope.ConfigurationRead, MachineScope.ConfigurationWrite, MachineScope.KeyValueManage })]
    [InlineData("""["annotationwrite", "CONFIGURATIONREAD", "annotationManage"]""", new[] { MachineScope.AnnotationRead, MachineScope.AnnotationWrite, MachineScope.ConfigurationRead, MachineScope.AnnotationManage })]
    [InlineData("""[1, "ConfigurationRead"]""", new[] { MachineScope.AnnotationRead, MachineScope.AnnotationWrite, MachineScope.ConfigurationRead })]
    public async Task PostMachines_Scopes_ArePassedDistinctInEnumOrderWithTheImpliedReads(string list, MachineScope[] expected)
    {
        var access = new RecordingAccess(Machine()) { Role = AccountRole.Owner };
        var http = CreateHttp(access);
        var body = $$"""{ "Organization": "", "Project": "", "Scopes": {{list}} }""";

        var result = await http.PostMachines(ContributorRequest(body), "org1", "proj1");

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<MachineAccess>().Scopes.ShouldBe(expected);
        access.Created.ShouldNotBeNull();
        access.Created.Scopes.ShouldBe(expected);
    }

    [Fact]
    public async Task PostMachines_EveryScopeInReverse_IsPassedInEnumOrder()
    {
        var access = new RecordingAccess(Machine()) { Role = AccountRole.Owner };
        var http = CreateHttp(access);
        var model = MachineCreate(null) with { Scopes = MachineScopes.All.Reverse().ToList() };

        await http.PostMachines(ContributorRequest(FunctionTestDoubles.ToJsonBody(model)), "org1", "proj1");

        access.Created.ShouldNotBeNull();
        access.Created.Scopes.ShouldBe(MachineScopes.All);
    }

    // The response carries the scopes as enum names, in enum order: the wire format of the SDKs.
    [Fact]
    public async Task PostMachines_Response_CarriesTheScopesAsEnumNames()
    {
        var access = new RecordingAccess(Machine());
        var http = CreateHttp(access);
        const string body = """{ "Organization": "", "Project": "", "Scopes": ["ConfigurationWrite", "AnnotationRead"] }""";

        var result = await http.PostMachines(ContributorRequest(body), "org1", "proj1");

        using var json = JsonDocument.Parse(FunctionTestDoubles.ToJsonBody(result.ShouldBeOfType<OkObjectResult>().Value));
        json.RootElement.GetProperty("Scopes").EnumerateArray().Select(scope => scope.GetString())
            .ShouldBe(["AnnotationRead", "ConfigurationRead", "ConfigurationWrite"]);
    }

    // A machine is exactly its scopes, so one without a runtime scope can do nothing: a missing list, an empty one and
    // management scopes alone, which grant nothing without the runtime scope of their operation. A body of the old shape
    // has no Scopes; its Scope and ManagementScopes are unknown properties. The 400 comes before the role rule, so a
    // Contributor gets it for management scopes too.
    [Theory]
    [InlineData("""{ "Organization": "", "Project": "" }""")]
    [InlineData("""{ "Organization": "", "Project": "", "Scopes": null }""")]
    [InlineData("""{ "Organization": "", "Project": "", "Scopes": [] }""")]
    [InlineData("""{ "Organization": "", "Project": "", "Scopes": ["ConfigurationTransfer"] }""")]
    [InlineData("""{ "Organization": "", "Project": "", "Scopes": ["ConfigurationTransfer", "SchemaWrite", "TemplateWrite", "AnnotationManage", "KeyValueManage"] }""")]
    [InlineData("""{ "Organization": "", "Project": "", "Scope": "DefaultReadWrite", "ManagementScopes": ["ConfigurationTransfer"] }""")]
    public async Task PostMachines_WithoutARuntimeScope_IsBadRequestAndNoMachineIsCreated(string body)
    {
        var access = new RecordingAccess(Machine());
        var http = CreateHttp(access);

        var result = await http.PostMachines(ContributorRequest(body), "org1", "proj1");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<Models.ErrorResponse>();
        error.Message.ShouldBe(NoRuntimeScope);
        error.ErrorCode.ShouldBeNull();
        access.Created.ShouldBeNull();
        access.Roles.ShouldBe(RoleLookups("org1.proj1"));
    }

    // The JSON reads integers as enum values, so an undefined one reaches the handler; it is named before the list is
    // checked for a runtime scope.
    [Theory]
    [InlineData("[99]", "The machine scope 99 doesn't exist.")]
    [InlineData("""["SchemaWrite", 11]""", "The machine scope 11 doesn't exist.")]
    [InlineData("""["AnnotationRead", -1]""", "The machine scope -1 doesn't exist.")]
    public async Task PostMachines_UndefinedScope_IsBadRequestAndNoMachineIsCreated(string list, string message)
    {
        var access = new RecordingAccess(Machine()) { Role = AccountRole.Administrator };
        var http = CreateHttp(access);
        var body = $$"""{ "Organization": "", "Project": "", "Scopes": {{list}} }""";

        var result = await http.PostMachines(ContributorRequest(body), "org1", "proj1");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<Models.ErrorResponse>();
        error.Message.ShouldBe(message);
        error.ErrorCode.ShouldBeNull();
        access.Created.ShouldBeNull();
    }

    // Spec 4: the creator of a machine can use its credentials, so its scopes may not exceed what the creator may do or
    // grant. Runtime scopes need the Contributor of the route, a secrets scope ContributorWithSecrets, a management scope
    // Administrator; the highest one counts, and the implied reads are part of it (SecretsWrite brings SecretsRead).
    // Below Contributor the route itself refuses. Debug builds (DEV_AUTH) skip the role checks of users, so there every
    // creation passes.
    [Theory]
    [MemberData(nameof(RequestedScopes))]
    public async Task PostMachines_TheScopes_NeedTheRoleOfTheirHighestGroup(MachineScope[] scopes, AccountRole needed, AccountRole role)
    {
        var access = new RecordingAccess(Machine()) { Role = role };
        var http = CreateHttp(access);
        var body = FunctionTestDoubles.ToJsonBody(MachineCreate(null) with { Scopes = scopes });

        if (role >= needed || DevAuth)
        {
            (await http.PostMachines(ContributorRequest(body), "org1", "proj1")).ShouldBeOfType<OkObjectResult>();
            access.Created.ShouldNotBeNull();
            return;
        }

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => http.PostMachines(ContributorRequest(body), "org1", "proj1"));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe($"No {Refusing(role, needed):G} access to 'org1.proj1'.");
        access.Created.ShouldBeNull();
    }

    // A new secret gives out the scopes of the stored machine, so the reset needs the role they need (spec 4), from one
    // point read of the stored machine. Debug builds (DEV_AUTH) skip the role checks of users, so there every reset passes.
    [Theory]
    [MemberData(nameof(StoredScopes))]
    public async Task PutMachine_TheStoredScopes_NeedTheRoleOfTheirHighestGroup(MachineScope[] stored, AccountRole needed, AccountRole role)
    {
        var access = new RecordingAccess(Machine() with { Scopes = stored }) { Role = role };
        var http = CreateHttp(access);

        if (role >= needed || DevAuth)
        {
            var reset = await http.PutMachine(ContributorRequest(), "org1", "proj1", KeycloakId);

            reset.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<MachineAccess>().AccessKey.ShouldBe("new-secret");
            access.Calls.ShouldBe([$"get:org1/proj1/{KeycloakId}", $"reset:org1/proj1/{KeycloakId}"]);
            return;
        }

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => http.PutMachine(ContributorRequest(), "org1", "proj1", KeycloakId));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe($"No {Refusing(role, needed):G} access to 'org1.proj1'.");
        access.Calls.ShouldNotContain($"reset:org1/proj1/{KeycloakId}");
    }

    // A machine that is not stored has no scopes to check; the reset answers as it always did.
    [Fact]
    public async Task PutMachine_OfAnUnknownMachine_IsLeftToTheService()
    {
        var access = new RecordingAccess(null);
        var http = CreateHttp(access);

        await Should.ThrowAsync<NotFoundException>(() => http.PutMachine(ContributorRequest(), "org1", "proj1", KeycloakId));

        access.Calls.ShouldBe([$"get:org1/proj1/{KeycloakId}", $"reset:org1/proj1/{KeycloakId}"]);
    }

    // Spec 4: reading, listing and deleting a machine stay at Contributor, whatever its scopes. Debug builds (DEV_AUTH)
    // skip the role checks of users.
    [Theory]
    [InlineData(AccountRole.Reader, false)]
    [InlineData(AccountRole.Contributor, true)]
    public async Task GetListAndDelete_OfAMachineWithEveryScope_NeedOnlyAContributor(AccountRole role, bool allowed)
    {
        var access = new RecordingAccess(Machine() with { Scopes = MachineScopes.All }) { Role = role };
        var http = CreateHttp(access);

        if (allowed || DevAuth)
        {
            (await http.GetMachines(ContributorRequest(), "org1", "proj1")).ShouldBeOfType<OkObjectResult>();
            (await http.GetMachine(ContributorRequest(), "org1", "proj1", KeycloakId)).ShouldBeOfType<OkObjectResult>();
            (await http.DeleteMachine(ContributorRequest(), "org1", "proj1", KeycloakId)).ShouldBeOfType<OkResult>();

            access.Calls.ShouldBe(["list:org1/proj1", $"get:org1/proj1/{KeycloakId}", $"delete:org1/proj1/{KeycloakId}"]);
            access.Roles.ShouldBe(RoleLookups("org1.proj1", "org1.proj1", "org1.proj1"));
            return;
        }

        foreach (var call in new Func<Task>[]
                 {
                     () => http.GetMachines(ContributorRequest(), "org1", "proj1"),
                     () => http.GetMachine(ContributorRequest(), "org1", "proj1", KeycloakId),
                     () => http.DeleteMachine(ContributorRequest(), "org1", "proj1", KeycloakId),
                 })
        {
            (await Should.ThrowAsync<AccessDeniedException>(call)).Message.ShouldBe("No Contributor access to 'org1.proj1'.");
        }

        access.Calls.ShouldBeEmpty();
    }

    // Machines are managed by users (spec 7.4): every machine route refuses a machine in every build, also one whose
    // credentials carry every scope and User.Impersonation, before a machine or a policy is read.
    [Fact]
    public async Task MachineRoutes_Machine_IsAccessDeniedInEveryBuild()
    {
        var access = new RecordingAccess(Machine()) { Role = AccountRole.Owner };
        var store = new PolicyStore();
        var http = CreateHttp(access, store);
        var create = FunctionTestDoubles.ToJsonBody(MachineCreate(null));
        var policy = FunctionTestDoubles.ToJsonBody(new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.ApiKey });

        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.GetMachines(UserOnlyRoutes.Machine(), "org1", "proj1"));
        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.GetMachine(UserOnlyRoutes.Machine(), "org1", "proj1", KeycloakId));
        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.PostMachines(UserOnlyRoutes.Machine("POST", create), "org1", "proj1"));
        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.PutMachine(UserOnlyRoutes.Machine("PUT"), "org1", "proj1", KeycloakId));
        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.DeleteMachine(UserOnlyRoutes.Machine("DELETE"), "org1", "proj1", KeycloakId));
        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.GetMachineAuthentication(UserOnlyRoutes.Machine(), "org1.proj1"));
        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.PutMachineAuthentication(UserOnlyRoutes.Machine("PUT", policy), "org1.proj1"));

        access.Calls.ShouldBeEmpty();
        access.Roles.ShouldBeEmpty();
        access.Created.ShouldBeNull();
        store.Reads.ShouldBeEmpty();
        store.Stored.ShouldBeNull();
    }

    // Data scopes in a user token do not stand in for User.Impersonation: 401 in Release builds. Debug builds (DEV_AUTH)
    // skip the check and serve the request.
    [Fact]
    public async Task PostMachines_TokenThatDoesNotActForAUser_IsUnauthorized()
    {
        var access = new RecordingAccess(Machine()) { Role = AccountRole.Owner };
        var http = CreateHttp(access);
        var request = UserOnlyRoutes.NotActingForAUser("POST", FunctionTestDoubles.ToJsonBody(MachineCreate(null)));

        if (DevAuth)
        {
            (await http.PostMachines(request, "org1", "proj1")).ShouldBeOfType<OkObjectResult>();
            return;
        }

        var exception = await Should.ThrowAsync<SecurityTokenException>(() => http.PostMachines(request, "org1", "proj1"));

        exception.Message.ShouldBe(UserOnlyRoutes.NotActingForAUserMessage);
        access.Created.ShouldBeNull();
        access.Roles.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", "The machine authentication policy is missing.")]
    [InlineData("null", "The machine authentication policy is missing.")]
    [InlineData("[1, 2]", "The request body is not a machine authentication policy (at $).")]
    public async Task PutMachineAuthentication_MissingOrInvalidBody_IsBadRequestAndNotStored(string body, string message)
    {
        var store = new PolicyStore();
        var http = CreateHttp(Administrator(), store);

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

    private static RecordingAccess Administrator() => new(Machine()) { Role = AccountRole.Administrator };

    private static TheoryData<MachineScope[], AccountRole, AccountRole> WithEveryRole(params (MachineScope[] Scopes, AccountRole Needed)[] rows)
    {
        var cases = new TheoryData<MachineScope[], AccountRole, AccountRole>();

        foreach (var (scopes, needed) in rows)
        {
            foreach (var role in new[] { AccountRole.Reader, AccountRole.Contributor, AccountRole.ContributorWithSecrets, AccountRole.Administrator, AccountRole.Owner })
            {
                cases.Add(scopes, needed, role);
            }
        }

        return cases;
    }

    // The role a refusal names: the Contributor of the route comes first, then the role of the scopes.
    private static AccountRole Refusing(AccountRole role, AccountRole needed) => role < AccountRole.Contributor ? AccountRole.Contributor : needed;

    // The role lookups of the user checks: Release builds make them, Debug builds (DEV_AUTH) skip them.
    private static string[] RoleLookups(params string[] accessPointKeys) => DevAuth ? [] : accessPointKeys;

    // A user's request (the role is the one of RecordingAccess); a body goes as JSON, with its content type.
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
            Scopes = [MachineScope.AnnotationRead, MachineScope.ConfigurationRead],
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
            Scopes = [MachineScope.ConfigurationRead],
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

    // One stored machine, or none. The role lookups of the user checks are kept apart from the machine calls, so the
    // calls compare alike in Debug and Release builds.
    private sealed class RecordingAccess(MachineAccess? machine) : IAccessService
    {
        public List<string> Calls { get; } = [];

        // The access points whose role the user checks looked up, one entry per check.
        public List<string> Roles { get; } = [];

        // The role of every account; checked in Release builds only.
        public AccountRole Role { get; init; } = AccountRole.Contributor;

        // The model the handler passed to CreateMachineAccessAsync; null while no machine was created.
        public MachineAccessCreate? Created { get; private set; }

        public Task<AccountRole> GetAccountRoleAsync(string accountId, string accessPointKey)
        {
            Roles.Add(accessPointKey);
            return Task.FromResult(Role);
        }

        public Task<MachineAccess?> GetMachineAccessAsync(string organization, string project, string id)
        {
            Calls.Add($"get:{organization}/{project}/{id}");
            return Task.FromResult(machine);
        }

        public Task<IEnumerable<MachineAccess>> GetMachineAccessesAsync(string organization, string project)
        {
            Calls.Add($"list:{organization}/{project}");
            return Task.FromResult<IEnumerable<MachineAccess>>(machine is null ? [] : [machine]);
        }

        // As the Cosmos service: an unknown machine is not found.
        public Task<MachineAccess> ResetMachineAccessAsync(string organization, string project, string appId)
        {
            Calls.Add($"reset:{organization}/{project}/{appId}");
            return machine is null
                ? Task.FromException<MachineAccess>(new NotFoundException($"The machine access {appId} has not been found."))
                : Task.FromResult(machine with { AccessKey = "new-secret" });
        }

        public Task<bool> DeleteMachineAccessAsync(string organization, string project, string appId)
        {
            Calls.Add($"delete:{organization}/{project}/{appId}");
            return Task.FromResult(true);
        }

        // Every service returns exactly the requested scopes (spec 3.5).
        public Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model)
        {
            Calls.Add($"create:{model.Organization}/{model.Project}");
            Created = model;
            return Task.FromResult((machine ?? Machine()) with { Scopes = model.Scopes ?? [], AnnotationKey = model.AnnotationKey });
        }

        public Task<Account?> GetAccountAsync(string id) => throw new NotImplementedException();

        public Task<Account> CreateAccountAsync(AccountCreate model) => throw new NotImplementedException();

        public Task<IEnumerable<AccessPoint>> GetAccessPointsAsync(string accountId) => throw new NotImplementedException();

        public Task<AccessPoint?> GetAccessPointAsync(string key) => throw new NotImplementedException();

        public Task<AccessPoint> CreateAccessPointAsync(AccessPointCreate model) => throw new NotImplementedException();

        public Task<bool> GrantPermissionAsync(Permission model) => throw new NotImplementedException();

        public Task<bool> RevokePermissionAsync(Permission model) => throw new NotImplementedException();

        public Task<bool> VerifyAccessForMachineAsync(string organization, string project, string appId) => throw new NotImplementedException();

        public Task<IReadOnlyList<AccessPointMember>> GetMembersAsync(string accessPointKey, string actorId) => throw new NotImplementedException();

        public Task<AccessPointMember> SetMemberRoleAsync(string accessPointKey, string accountId, AccountRole role, string actorId) => throw new NotImplementedException();

        public Task RemoveMemberAsync(string accessPointKey, string accountId, string actorId) => throw new NotImplementedException();

        public Task<AccountRole> JoinAccessPointAsync(string accessPointKey, string accountId, AccountRole role) => throw new NotImplementedException();
    }
}
