using System.Collections.Specialized;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;
using _42.Platform.Storyteller.KeyValues;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The key/value endpoints: who the caller is, the mTLS rule, the query parsing, and what the responses carry.
// The role refusals of users (403 from CheckAccessToProjectAsync) are skipped in a Debug build (DEV_AUTH); the scopes
// and the secret rights of machines are checked in every build.
//
// Every handler a machine calls is a row of MachineHandlers with its machine scopes (spec 7.5), in the order they are
// checked: the runtime scope, the management scope of a management operation, and Secrets.Write for protect. Secret
// values need a secrets scope, stored on the machine and carried by its credentials: Secrets.Read to reveal, diff and
// export them, Secrets.Write to write them and to protect. Users carry no data scope: the role decides, and only
// ContributorWithSecrets gives them the secret rights. The Administrator operations are for users only.
public class KeyValuesHttpTests
{
    private const string Org = "acme";
    private const string Project = "billing";
    private const string View = "default";

    // The refusals of the secret rights: by the handler, and by the service for a secret it was asked for.
    private const string NoSecretRights = "The caller has no rights to the secrets of the project.";
    private const string NoRightsToTheSecret = "The key 'db.password' is a secret; the caller has no rights to secrets.";

    // The handlers a machine calls, with valid bodies; a new one belongs here.
    private static readonly ScopedHandler[] MachineHandlers =
    [
        new(nameof(KeyValuesHttp.GetKeyValues), "GET", null, (http, request) => http.KeyValues.GetKeyValues(request, Org, Project, View), MachineScope.ConfigurationRead),
        new(nameof(KeyValuesHttp.GetKeyValue), "GET", null, (http, request) => http.KeyValues.GetKeyValue(request, Org, Project, View, "k"), MachineScope.ConfigurationRead),
        new(nameof(KeyValuesHttp.GetKeyValueVersions), "GET", null, (http, request) => http.KeyValues.GetKeyValueVersions(request, Org, Project, View, "k"), MachineScope.ConfigurationRead),
        new(nameof(KeyValuesHttp.GetKeyValueVersion), "GET", null, (http, request) => http.KeyValues.GetKeyValueVersion(request, Org, Project, View, "k", 2), MachineScope.ConfigurationRead),
        new(nameof(KeyValuesHttp.GetKeyValueVersionDiff), "GET", null, (http, request) => http.KeyValues.GetKeyValueVersionDiff(request, Org, Project, View, "k", 2), MachineScope.ConfigurationRead),
        new(nameof(KeyValuesHttp.GetKeyValueVersionDiffCustom), "GET", null, (http, request) => http.KeyValues.GetKeyValueVersionDiffCustom(request, Org, Project, View, "k", 3, 1), MachineScope.ConfigurationRead),
        new(nameof(KeyValuePolicyHttp.GetKeyValuePolicy), "GET", null, (http, request) => http.Policy.GetKeyValuePolicy(request, Org, Project), MachineScope.ConfigurationRead),
        new(nameof(KeyValuePolicyHttp.GetRecipients), "GET", null, (http, request) => http.Policy.GetRecipients(request, Org, Project), MachineScope.ConfigurationRead),
        new(nameof(KeyValuesHttp.SetKeyValue), "PUT", FunctionTestDoubles.ToJsonBody(new KeyValueWrite { Protection = KeyValueProtection.Public, Value = "plain" }), (http, request) => http.KeyValues.SetKeyValue(request, Org, Project, View, "k"), MachineScope.ConfigurationWrite),
        new(nameof(KeyValuesHttp.DeleteKeyValue), "DELETE", null, (http, request) => http.KeyValues.DeleteKeyValue(request, Org, Project, View, "k"), MachineScope.ConfigurationWrite),
        new(nameof(KeyValuesHttp.RestoreKeyValues), "POST", FunctionTestDoubles.ToJsonBody(new KeyValueBulkRequest { Keys = ["a"] }), (http, request) => http.KeyValues.RestoreKeyValues(request, Org, Project, View), MachineScope.ConfigurationWrite, MachineScope.KeyValueManage),
        new(nameof(KeyValuesHttp.ProtectKeyValues), "POST", FunctionTestDoubles.ToJsonBody(new KeyValueBulkRequest { Keys = ["a"], Protection = KeyValueProtection.Hsm }), (http, request) => http.KeyValues.ProtectKeyValues(request, Org, Project, View), MachineScope.ConfigurationWrite, MachineScope.KeyValueManage, MachineScope.SecretsWrite),
        new(nameof(KeyValuesHttp.ExportKeyValues), "POST", FunctionTestDoubles.ToJsonBody(new KeyValueExportRequest()), (http, request) => http.KeyValues.ExportKeyValues(request, Org, Project, View), MachineScope.ConfigurationRead, MachineScope.ConfigurationTransfer),
        new(nameof(KeyValuesHttp.ImportKeyValues), "POST", FunctionTestDoubles.ToJsonBody(new KeyValueDocument { Items = [new KeyValueDocumentItem { Key = "a", Protection = KeyValueProtection.Public, Value = "1" }] }), (http, request) => http.KeyValues.ImportKeyValues(request, Org, Project, View), MachineScope.ConfigurationWrite, MachineScope.ConfigurationTransfer),
    ];

    // The Administrator operations: the policy, purges, permanent deletes, the audit trail and usage.
    private static readonly Handler[] AdministratorHandlers =
    [
        new(nameof(KeyValuesHttp.PurgeKeyValues), "POST", FunctionTestDoubles.ToJsonBody(new KeyValueBulkRequest { Keys = ["a"] }), (http, request) => http.KeyValues.PurgeKeyValues(request, Org, Project, View)),
        new($"{nameof(KeyValuesHttp.DeleteKeyValue)} permanent", "DELETE", null, (http, request) => http.KeyValues.DeleteKeyValue(Permanent(request), Org, Project, View, "k")),
        new(nameof(KeyValuePolicyHttp.SetKeyValuePolicy), "PUT", FunctionTestDoubles.ToJsonBody(new KeyValuePolicyUpdate { RequireMtlsForSecretDelivery = false }), (http, request) => http.Policy.SetKeyValuePolicy(request, Org, Project)),
        new(nameof(KeyValuePolicyHttp.GetByokSetup), "GET", null, (http, request) => http.Policy.GetByokSetup(request, Org, Project)),
        new(nameof(KeyValuePolicyHttp.VerifyByok), "POST", null, (http, request) => http.Policy.VerifyByok(request, Org, Project)),
        new(nameof(KeyValuePolicyHttp.RotateKeyValueKey), "POST", FunctionTestDoubles.ToJsonBody(new KeyValueRotateRequest { Protection = KeyValueProtection.Hsm }), (http, request) => http.Policy.RotateKeyValueKey(request, Org, Project)),
        new(nameof(KeyValuePolicyHttp.AddRecipient), "POST", FunctionTestDoubles.ToJsonBody(new ClientEncryptionRecipientCreate { Name = "api", PublicKey = "-----BEGIN PUBLIC KEY-----" }), (http, request) => http.Policy.AddRecipient(request, Org, Project)),
        new(nameof(KeyValuePolicyHttp.UpdateRecipient), "PATCH", FunctionTestDoubles.ToJsonBody(new ClientEncryptionRecipientUpdate { Name = "api2" }), (http, request) => http.Policy.UpdateRecipient(request, Org, Project, "r1")),
        new(nameof(KeyValuePolicyHttp.RemoveRecipient), "DELETE", null, (http, request) => http.Policy.RemoveRecipient(request, Org, Project, "r1")),
        new(nameof(KeyValueUsageHttp.GetKeyValueAudit), "GET", null, (http, request) => http.Usage.GetKeyValueAudit(request, Org, Project)),
        new(nameof(KeyValueUsageHttp.GetKeyValueUsage), "GET", null, (http, request) => http.Usage.GetKeyValueUsage(request, Org, Project)),
    ];

    public static TheoryData<string> MachineHandlerNames => new(MachineHandlers.Select(handler => handler.Name));

    public static TheoryData<string> ManagementHandlerNames => new(MachineHandlers.Where(handler => handler.ManagementScope is not null).Select(handler => handler.Name));

    public static TheoryData<string> AdministratorHandlerNames => new(AdministratorHandlers.Select(handler => handler.Name));

    private static bool DevAuth => HttpRequestDataExtensions.IsDevelopmentAuthentication;

    [Fact]
    public async Task GetKeyValue_RevealByAUserWithSecrets_PassesACallerWithRights()
    {
        var (http, service) = Create(AccountRole.ContributorWithSecrets);
        service.Pair = RecordingKeyValueService.Secret("db.password", "s3cret");

        var result = await http.GetKeyValue(KeyValueRequests.User(query: q => q["reveal"] = "true"), Org, Project, View, "db.password");

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<KeyValue>().Value.ShouldBe("s3cret");
        service.LastRead!.Reveal.ShouldBeTrue();
        service.LastCaller!.CanReadSecrets.ShouldBeTrue();
        service.LastCaller.Channel.ShouldBe(KeyValueChannel.User);
        service.LastCaller.CorrelationId.ShouldBe("invocation");
    }

    [Fact]
    public async Task GetKeyValue_RevealByAMachineWithoutMtls_IsMtlsRequired()
    {
        var (http, service) = Create(AccountRole.None, requireMtls: true);
        service.Pair = RecordingKeyValueService.Secret("db.password", "s3cret");

        var act = () => http.GetKeyValue(KeyValueRequests.Machine(mtls: false, query: q => q["reveal"] = "true"), Org, Project, View, "db.password");

        (await act.ShouldThrowAsync<AccessDeniedException>()).ErrorCode.ShouldBe(ErrorCodes.MtlsRequired);
        service.Calls.ShouldBeEmpty("the rule is decided before the service is asked");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task GetKeyValue_RevealByAMachine_FollowsTheProjectRule(bool mtls, bool requireMtls)
    {
        var (http, service) = Create(AccountRole.None, requireMtls);
        service.Pair = RecordingKeyValueService.Secret("db.password", "s3cret");

        var result = await http.GetKeyValue(KeyValueRequests.Machine(mtls, query: q => q["reveal"] = "true"), Org, Project, View, "db.password");

        result.ShouldBeOfType<OkObjectResult>();
        service.LastCaller!.CanReadSecrets.ShouldBeTrue();
        service.LastCaller.Channel.ShouldBe(mtls ? KeyValueChannel.MachineMtls : KeyValueChannel.MachineToken);
    }

    [Fact]
    public async Task GetKeyValue_WithoutReveal_NeverNeedsRights()
    {
        var (http, service) = Create(AccountRole.Reader);
        service.Pair = RecordingKeyValueService.Secret("db.password", "s3cret");

        var result = await http.GetKeyValue(KeyValueRequests.Machine(mtls: false, roles: [Scopes.Configuration.Read]), Org, Project, View, "db.password");

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<KeyValue>().Value.ShouldBeNull();
    }

    [Fact]
    public async Task GetKeyValue_Missing_IsNotFoundWithTheCode()
    {
        var (http, service) = Create(AccountRole.Reader);
        service.Pair = null;

        var result = await http.GetKeyValue(KeyValueRequests.User(), Org, Project, View, "missing");

        result.ShouldBeOfType<NotFoundObjectResult>().Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.KeyValueNotFound);
    }

    [Fact]
    public async Task SetKeyValue_NeverReturnsTheSecretValue()
    {
        var (http, _) = Create(AccountRole.ContributorWithSecrets);

        var secret = await http.SetKeyValue(KeyValueRequests.User("PUT", body: FunctionTestDoubles.ToJsonBody(new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "s3cret" })), Org, Project, View, "db.password");
        var @public = await http.SetKeyValue(KeyValueRequests.User("PUT", body: FunctionTestDoubles.ToJsonBody(new KeyValueWrite { Protection = KeyValueProtection.Public, Value = "plain" })), Org, Project, View, "db.host");

        secret.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<KeyValue>().Value.ShouldBeNull();
        @public.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<KeyValue>().Value.ShouldBe("plain");
    }

    [Fact]
    public async Task SetKeyValue_WithoutABody_IsBadRequest()
    {
        var (http, service) = Create(AccountRole.Contributor);

        var result = await http.SetKeyValue(KeyValueRequests.User("PUT"), Org, Project, View, "k");

        result.ShouldBeOfType<BadRequestObjectResult>();
        service.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetKeyValue_SecretByAMachineWithoutMtls_TurnsTheRefusalIntoMtlsRequired()
    {
        var (http, _) = Create(AccountRole.None, requireMtls: true);

        var act = () => http.SetKeyValue(KeyValueRequests.Machine(mtls: false, "PUT", body: FunctionTestDoubles.ToJsonBody(new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "s" })), Org, Project, View, "db.password");

        (await act.ShouldThrowAsync<AccessDeniedException>()).ErrorCode.ShouldBe(ErrorCodes.MtlsRequired);
    }

    // A secret level is a secret write: Secrets.Write, stored on the machine and carried by its credentials. Secrets.Read
    // does not stand in for it, and the refusal of the service stays AccessDenied.
    [Theory]
    [InlineData(new[] { MachineScope.SecretsWrite }, new[] { MachineScope.SecretsWrite }, true)]
    [InlineData(new[] { MachineScope.SecretsRead }, new[] { MachineScope.SecretsRead }, false)]
    [InlineData(new[] { MachineScope.SecretsWrite }, new MachineScope[0], false)]
    [InlineData(new MachineScope[0], new[] { MachineScope.SecretsWrite }, false)]
    public async Task SetKeyValue_SecretByAMachine_NeedsSecretsWriteStoredAndCarried(MachineScope[] stored, MachineScope[] carried, bool isAllowed)
    {
        var (http, service) = Create(AccountRole.None, scopes: [MachineScope.ConfigurationWrite, .. stored]);
        var body = FunctionTestDoubles.ToJsonBody(new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "s3cret" });
        var act = () => http.SetKeyValue(MachineOverMtls([MachineScope.ConfigurationWrite, .. carried], "PUT", body: body), Org, Project, View, "db.password");

        if (isAllowed)
        {
            (await act()).ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<KeyValue>().Value.ShouldBeNull();
            service.LastCaller!.CanWriteSecrets.ShouldBeTrue();
            return;
        }

        var exception = await act.ShouldThrowAsync<AccessDeniedException>();
        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe(NoRightsToTheSecret);
    }

    // The other writes leave secret keys and items to the service, with the write right of Secrets.Write alone:
    // Secrets.Read gives the read right only.
    [Theory]
    [InlineData(nameof(KeyValuesHttp.DeleteKeyValue), MachineScope.SecretsWrite, false, true)]
    [InlineData(nameof(KeyValuesHttp.DeleteKeyValue), MachineScope.SecretsRead, true, false)]
    [InlineData(nameof(KeyValuesHttp.RestoreKeyValues), MachineScope.SecretsWrite, false, true)]
    [InlineData(nameof(KeyValuesHttp.RestoreKeyValues), MachineScope.SecretsRead, true, false)]
    [InlineData(nameof(KeyValuesHttp.ImportKeyValues), MachineScope.SecretsWrite, false, true)]
    [InlineData(nameof(KeyValuesHttp.ImportKeyValues), MachineScope.SecretsRead, true, false)]
    public async Task Write_ByAMachine_PassesTheSecretRightOfEachSecretsScope(string handler, MachineScope secrets, bool canRead, bool canWrite)
    {
        var scoped = Scoped(handler);
        MachineScope[] scopes = [.. scoped.Scopes, secrets];
        var http = Handlers(scopes);

        await scoped.Invoke(http, Request(scoped, scopes, mtls: true));

        http.Service.LastCaller!.CanReadSecrets.ShouldBe(canRead);
        http.Service.LastCaller.CanWriteSecrets.ShouldBe(canWrite);
    }

    [Fact]
    public async Task DeleteKeyValue_SoftAndPermanent()
    {
        var (http, service) = Create(AccountRole.Administrator);

        var soft = await http.DeleteKeyValue(KeyValueRequests.User("DELETE", query: q => q["expectedVersion"] = "3"), Org, Project, View, "k");
        var permanent = await http.DeleteKeyValue(KeyValueRequests.User("DELETE", query: q => q["permanent"] = "true"), Org, Project, View, "k");
        service.DeleteResult = false;
        var missing = await http.DeleteKeyValue(KeyValueRequests.User("DELETE"), Org, Project, View, "k");

        soft.ShouldBeOfType<NoContentResult>();
        permanent.ShouldBeOfType<NoContentResult>();
        missing.ShouldBeOfType<NotFoundObjectResult>();
        service.Calls.Count.ShouldBe(3);
        service.LastDelete!.Permanent.ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteKeyValue_PermanentByAContributor_IsRefused()
    {
        if (DevAuth)
        {
            return;
        }

        var (http, _) = Create(AccountRole.Contributor);

        var act = () => http.DeleteKeyValue(KeyValueRequests.User("DELETE", query: q => q["permanent"] = "true"), Org, Project, View, "k");

        await act.ShouldThrowAsync<AccessDeniedException>();
    }

    [Fact]
    public async Task Bulk_NeedsKeysOrAPrefix_AndProtectNeedsTheLevel()
    {
        var (http, service) = Create(AccountRole.ContributorWithSecrets);

        var noKeys = await http.RestoreKeyValues(KeyValueRequests.User("POST", body: FunctionTestDoubles.ToJsonBody(new KeyValueBulkRequest())), Org, Project, View);
        var noLevel = await http.ProtectKeyValues(KeyValueRequests.User("POST", body: FunctionTestDoubles.ToJsonBody(new KeyValueBulkRequest { Keys = ["a"] })), Org, Project, View);
        var restored = await http.RestoreKeyValues(KeyValueRequests.User("POST", body: FunctionTestDoubles.ToJsonBody(new KeyValueBulkRequest { KeyPrefix = "db." })), Org, Project, View);
        var protectedResult = await http.ProtectKeyValues(KeyValueRequests.User("POST", body: FunctionTestDoubles.ToJsonBody(new KeyValueBulkRequest { Keys = ["a", "b"], Protection = KeyValueProtection.Hsm })), Org, Project, View);

        noKeys.ShouldBeOfType<BadRequestObjectResult>();
        noLevel.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.ProtectionRequired);
        restored.ShouldBeOfType<OkObjectResult>();
        protectedResult.ShouldBeOfType<OkObjectResult>();
        service.Calls.ShouldBe(["restore:db.", "protect:a|b"]);
    }

    // The management scope adds to the rules of protect and replaces none of them: the mTLS rule still applies.
    [Fact]
    public async Task Protect_ByAMachineWithoutMtls_IsMtlsRequired()
    {
        var (http, service) = Create(AccountRole.None, requireMtls: true, managementScopes: [MachineScope.KeyValueManage]);

        var act = () => http.ProtectKeyValues(KeyValueRequests.Machine(mtls: false, "POST", body: FunctionTestDoubles.ToJsonBody(new KeyValueBulkRequest { Keys = ["a"], Protection = KeyValueProtection.Hsm }), managementScopes: [MachineScope.KeyValueManage]), Org, Project, View);

        (await act.ShouldThrowAsync<AccessDeniedException>()).ErrorCode.ShouldBe(ErrorCodes.MtlsRequired);
        service.Calls.ShouldBeEmpty();
    }

    // And the secret rights still apply: protect writes secrets, so it needs Secrets.Write, stored and carried. The
    // handler checks the write right before the service, which checks it again; KeyValue.Manage and Secrets.Read do
    // not stand in for it.
    [Theory]
    [InlineData(new[] { MachineScope.SecretsWrite }, new[] { MachineScope.SecretsWrite }, true)]
    [InlineData(new[] { MachineScope.SecretsRead }, new[] { MachineScope.SecretsRead }, false)]
    [InlineData(new[] { MachineScope.SecretsWrite }, new MachineScope[0], false)]
    [InlineData(new MachineScope[0], new[] { MachineScope.SecretsWrite }, false)]
    public async Task Protect_ByAMachine_NeedsSecretsWriteStoredAndCarried(MachineScope[] stored, MachineScope[] carried, bool isAllowed)
    {
        MachineScope[] scopes = [MachineScope.ConfigurationWrite, MachineScope.KeyValueManage];
        var (http, service) = Create(AccountRole.None, scopes: [.. scopes, .. stored]);
        var body = FunctionTestDoubles.ToJsonBody(new KeyValueBulkRequest { Keys = ["a"], Protection = KeyValueProtection.Hsm });
        var act = () => http.ProtectKeyValues(MachineOverMtls([.. scopes, .. carried], "POST", body: body), Org, Project, View);

        if (isAllowed)
        {
            (await act()).ShouldBeOfType<OkObjectResult>();
            service.Calls.ShouldBe(["protect:a"]);
            service.LastCaller!.CanWriteSecrets.ShouldBeTrue();
            return;
        }

        var exception = await act.ShouldThrowAsync<AccessDeniedException>();
        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe(NoSecretRights);
        service.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Export_WithSecrets_NeedsTheRights()
    {
        MachineScope[] transfer = [MachineScope.ConfigurationTransfer];
        var (http, service) = Create(AccountRole.None, requireMtls: true, managementScopes: transfer);

        var withoutSecrets = await http.ExportKeyValues(KeyValueRequests.Machine(mtls: false, "POST", managementScopes: transfer), Org, Project, View);
        var withMtls = await http.ExportKeyValues(KeyValueRequests.Machine(mtls: true, "POST", body: FunctionTestDoubles.ToJsonBody(new KeyValueExportRequest { IncludeSecrets = true }), managementScopes: transfer), Org, Project, View);
        var act = () => http.ExportKeyValues(KeyValueRequests.Machine(mtls: false, "POST", body: FunctionTestDoubles.ToJsonBody(new KeyValueExportRequest { IncludeSecrets = true }), managementScopes: transfer), Org, Project, View);

        withoutSecrets.ShouldBeOfType<OkObjectResult>();
        withMtls.ShouldBeOfType<OkObjectResult>();
        (await act.ShouldThrowAsync<AccessDeniedException>()).ErrorCode.ShouldBe(ErrorCodes.MtlsRequired);
        service.LastExport!.IncludeSecrets.ShouldBeTrue();
        service.Calls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Export_WithSecrets_ByAReader_IsAccessDenied()
    {
        if (DevAuth)
        {
            return;
        }

        var (http, _) = Create(AccountRole.Reader);

        var act = () => http.ExportKeyValues(KeyValueRequests.User("POST", body: FunctionTestDoubles.ToJsonBody(new KeyValueExportRequest { IncludeSecrets = true })), Org, Project, View);

        (await act.ShouldThrowAsync<AccessDeniedException>()).ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
    }

    // Exactly the scopes of the row, stored and carried, and nothing else: a write needs no read scope, and nothing
    // expands at request time.
    [Theory]
    [MemberData(nameof(MachineHandlerNames))]
    public async Task Handler_ByAMachineWithExactlyItsScopes_ReachesTheService(string handler)
    {
        var scoped = Scoped(handler);
        var http = Handlers(scoped.Scopes);

        var result = await scoped.Invoke(http, Request(scoped, scoped.Scopes, mtls: true));

        result.ShouldNotBeOfType<BadRequestObjectResult>();
        http.Calls.ShouldHaveSingleItem();
    }

    // Every other scope, the write of a read included, does not stand in for the runtime scope of the row. The call is
    // refused before the body is read and before any service call, and the AccessDenied of the missing scope is not
    // taken for a missing mTLS certificate.
    [Theory]
    [MemberData(nameof(MachineHandlerNames))]
    public async Task Handler_ByAMachineCreatedWithoutItsRuntimeScope_IsAccessDeniedBeforeTheService(string handler)
    {
        var scoped = Scoped(handler);
        var http = Handlers(Without(MachineScopes.All, scoped.Scope));

        await ShouldNeedTheScopeAsync(http, () => scoped.Invoke(http, Request(scoped, MachineScopes.All, mtls: false)), scoped.Scope, isStored: false);
    }

    // A federated token that asked for a subset, or a provider token without the permission or the role.
    [Theory]
    [MemberData(nameof(MachineHandlerNames))]
    public async Task Handler_ByAMachineWhoseCredentialsDoNotCarryItsRuntimeScope_IsAccessDeniedBeforeTheService(string handler)
    {
        var scoped = Scoped(handler);
        var http = Handlers(MachineScopes.All);

        await ShouldNeedTheScopeAsync(http, () => scoped.Invoke(http, Request(scoped, Without(MachineScopes.All, scoped.Scope), mtls: false)), scoped.Scope, isStored: true);
    }

    // The export and import need Configuration.Transfer, protect and the bulk restore KeyValue.Manage, on top of the
    // runtime scope, in every build.
    [Theory]
    [MemberData(nameof(ManagementHandlerNames))]
    public async Task ManagementHandler_ByAMachineCreatedWithoutItsManagementScope_IsAccessDeniedBeforeTheService(string handler)
    {
        var scoped = Scoped(handler);
        var http = Handlers(Without(MachineScopes.All, scoped.ManagementScope));

        await ShouldNeedTheScopeAsync(http, () => scoped.Invoke(http, Request(scoped, MachineScopes.All, mtls: false)), scoped.ManagementScope!.Value, isStored: false);
    }

    [Theory]
    [MemberData(nameof(ManagementHandlerNames))]
    public async Task ManagementHandler_ByAMachineWhoseCredentialsDoNotCarryItsManagementScope_IsAccessDeniedBeforeTheService(string handler)
    {
        var scoped = Scoped(handler);
        var http = Handlers(MachineScopes.All);

        await ShouldNeedTheScopeAsync(http, () => scoped.Invoke(http, Request(scoped, Without(MachineScopes.All, scoped.ManagementScope), mtls: false)), scoped.ManagementScope!.Value, isStored: true);
    }

    // A management scope grants nothing without the runtime scope of its operation, which is checked first.
    [Theory]
    [MemberData(nameof(ManagementHandlerNames))]
    public async Task ManagementHandler_ByAMachineWithItsManagementScopeAlone_NeedsTheRuntimeScopeFirst(string handler)
    {
        var scoped = Scoped(handler);
        MachineScope[] alone = [scoped.ManagementScope!.Value];
        var http = Handlers(alone);

        await ShouldNeedTheScopeAsync(http, () => scoped.Invoke(http, Request(scoped, alone, mtls: true)), scoped.Scope, isStored: false);
    }

    // Without both scopes the refusal names the runtime scope, which is checked first.
    [Theory]
    [MemberData(nameof(ManagementHandlerNames))]
    public async Task ManagementHandler_ByAMachineCreatedWithoutItsRuntimeAndManagementScopes_NeedsTheRuntimeScopeFirst(string handler)
    {
        var scoped = Scoped(handler);
        var http = Handlers(Without(MachineScopes.All, scoped.Scope, scoped.ManagementScope));

        await ShouldNeedTheScopeAsync(http, () => scoped.Invoke(http, Request(scoped, MachineScopes.All, mtls: true)), scoped.Scope, isStored: false);
    }

    [Theory]
    [MemberData(nameof(ManagementHandlerNames))]
    public async Task ManagementHandler_ByAMachineWhoseCredentialsCarryNeitherOfItsScopes_NeedsTheRuntimeScopeFirst(string handler)
    {
        var scoped = Scoped(handler);
        var http = Handlers(MachineScopes.All);

        await ShouldNeedTheScopeAsync(http, () => scoped.Invoke(http, Request(scoped, Without(MachineScopes.All, scoped.Scope, scoped.ManagementScope), mtls: true)), scoped.Scope, isStored: true);
    }

    // The other management scope of the key/values does not stand in for it.
    [Theory]
    [MemberData(nameof(ManagementHandlerNames))]
    public async Task ManagementHandler_ByAMachineWithTheOtherManagementScope_IsAccessDenied(string handler)
    {
        var scoped = Scoped(handler);
        var other = scoped.ManagementScope == MachineScope.KeyValueManage ? MachineScope.ConfigurationTransfer : MachineScope.KeyValueManage;
        MachineScope[] scopes = [.. Without(scoped.Scopes, scoped.ManagementScope), other];
        var http = Handlers(scopes);

        await ShouldNeedTheScopeAsync(http, () => scoped.Invoke(http, Request(scoped, scopes, mtls: true)), scoped.ManagementScope!.Value, isStored: false);
    }

    // Users carry no data scope: User.Impersonation and the role decide (the role check runs in Release builds only).
    // ContributorWithSecrets covers every row, protect too.
    [Theory]
    [MemberData(nameof(MachineHandlerNames))]
    public async Task Handler_ByAUserWithoutDataScopes_ReachesTheService(string handler)
    {
        var scoped = Scoped(handler);
        var http = Handlers([], AccountRole.ContributorWithSecrets);

        var result = await scoped.Invoke(http, KeyValueRequests.User(scoped.Method, body: scoped.Body));

        result.ShouldNotBeOfType<BadRequestObjectResult>();
        http.Calls.ShouldHaveSingleItem();
    }

    // The Administrator operations are for users only: a machine is refused in every build with the route's message,
    // whatever it was created with and its credentials carry, User.Impersonation included, before any service.
    [Theory]
    [MemberData(nameof(AdministratorHandlerNames))]
    public async Task AdministratorHandler_ByAMachine_IsRefusedBeforeTheService(string handler)
    {
        var operation = Administrator(handler);
        var http = Handlers(MachineScopes.All);
        var request = TestRequests.MachineCarrying([.. MachineScopes.ToScopeStrings(MachineScopes.All), Scopes.User.Impersonation], operation.Method, operation.Body, mtls: true);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => operation.Invoke(http, request));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("Only a user with the Administrator role may call this operation; machines cannot.");
        http.Calls.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(AdministratorHandlerNames))]
    public async Task AdministratorHandler_ByAnAdministrator_ReachesTheService(string handler)
    {
        var operation = Administrator(handler);
        var http = Handlers([], AccountRole.Administrator);

        var result = await operation.Invoke(http, KeyValueRequests.User(operation.Method, body: operation.Body));

        result.ShouldNotBeOfType<BadRequestObjectResult>();
        http.Calls.ShouldHaveSingleItem();
    }

    // A reveal reads a secret value: Secrets.Read, stored on the machine and carried by its credentials. Secrets.Write
    // does not stand in for it; the service refuses the secret.
    [Theory]
    [InlineData(new[] { MachineScope.SecretsRead }, new[] { MachineScope.SecretsRead }, true)]
    [InlineData(new[] { MachineScope.SecretsWrite }, new[] { MachineScope.SecretsWrite }, false)]
    [InlineData(new[] { MachineScope.SecretsRead }, new MachineScope[0], false)]
    [InlineData(new MachineScope[0], new[] { MachineScope.SecretsRead }, false)]
    public async Task GetKeyValue_RevealByAMachine_NeedsSecretsReadStoredAndCarried(MachineScope[] stored, MachineScope[] carried, bool isAllowed)
    {
        var (http, service) = Create(AccountRole.None, scopes: [MachineScope.ConfigurationRead, .. stored]);
        service.Pair = RecordingKeyValueService.Secret("db.password", "s3cret");
        var act = () => http.GetKeyValue(MachineOverMtls([MachineScope.ConfigurationRead, .. carried], query: q => q["reveal"] = "true"), Org, Project, View, "db.password");

        if (isAllowed)
        {
            (await act()).ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<KeyValue>().Value.ShouldBe("s3cret");
            return;
        }

        var exception = await act.ShouldThrowAsync<AccessDeniedException>();
        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe(NoRightsToTheSecret);
    }

    // The reveal of a version reads a secret value too: Secrets.Read, stored and carried.
    [Theory]
    [InlineData(new[] { MachineScope.SecretsRead }, new[] { MachineScope.SecretsRead }, true)]
    [InlineData(new[] { MachineScope.SecretsWrite }, new[] { MachineScope.SecretsWrite }, false)]
    [InlineData(new[] { MachineScope.SecretsRead }, new MachineScope[0], false)]
    [InlineData(new MachineScope[0], new[] { MachineScope.SecretsRead }, false)]
    public async Task GetKeyValueVersion_RevealByAMachine_NeedsSecretsReadStoredAndCarried(MachineScope[] stored, MachineScope[] carried, bool isAllowed)
    {
        var (http, service) = Create(AccountRole.None, scopes: [MachineScope.ConfigurationRead, .. stored]);
        service.Pair = RecordingKeyValueService.Secret("db.password", "s3cret");
        var act = () => http.GetKeyValueVersion(MachineOverMtls([MachineScope.ConfigurationRead, .. carried], query: q => q["reveal"] = "true"), Org, Project, View, "db.password", 2);

        if (isAllowed)
        {
            (await act()).ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<KeyValue>().Value.ShouldBe("s3cret");
            return;
        }

        var exception = await act.ShouldThrowAsync<AccessDeniedException>();
        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe(NoRightsToTheSecret);
    }

    // The diff of a secret carries its text: Secrets.Read too.
    [Theory]
    [InlineData(new[] { MachineScope.SecretsRead }, new[] { MachineScope.SecretsRead }, true)]
    [InlineData(new[] { MachineScope.SecretsWrite }, new[] { MachineScope.SecretsWrite }, false)]
    [InlineData(new[] { MachineScope.SecretsRead }, new MachineScope[0], false)]
    [InlineData(new MachineScope[0], new[] { MachineScope.SecretsRead }, false)]
    public async Task Diff_OfASecretByAMachine_NeedsSecretsReadStoredAndCarried(MachineScope[] stored, MachineScope[] carried, bool isAllowed)
    {
        var (http, service) = Create(AccountRole.None, scopes: [MachineScope.ConfigurationRead, .. stored]);
        service.Pair = RecordingKeyValueService.Secret("db.password", "s3cret");
        var act = () => http.GetKeyValueVersionDiff(MachineOverMtls([MachineScope.ConfigurationRead, .. carried]), Org, Project, View, "db.password", 2);

        if (isAllowed)
        {
            (await act()).ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<DiffResult>();
            return;
        }

        var exception = await act.ShouldThrowAsync<AccessDeniedException>();
        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe(NoRightsToTheSecret);
    }

    // An export with secrets reads secret values: Secrets.Read, checked before the service.
    [Theory]
    [InlineData(new[] { MachineScope.SecretsRead }, new[] { MachineScope.SecretsRead }, true)]
    [InlineData(new[] { MachineScope.SecretsWrite }, new[] { MachineScope.SecretsWrite }, false)]
    [InlineData(new[] { MachineScope.SecretsRead }, new MachineScope[0], false)]
    [InlineData(new MachineScope[0], new[] { MachineScope.SecretsRead }, false)]
    public async Task Export_WithSecretsByAMachine_NeedsSecretsReadStoredAndCarried(MachineScope[] stored, MachineScope[] carried, bool isAllowed)
    {
        MachineScope[] scopes = [MachineScope.ConfigurationRead, MachineScope.ConfigurationTransfer];
        var (http, service) = Create(AccountRole.None, scopes: [.. scopes, .. stored]);
        var body = FunctionTestDoubles.ToJsonBody(new KeyValueExportRequest { IncludeSecrets = true });
        var act = () => http.ExportKeyValues(MachineOverMtls([.. scopes, .. carried], "POST", body: body), Org, Project, View);

        if (isAllowed)
        {
            (await act()).ShouldBeOfType<OkObjectResult>();
            service.LastExport!.IncludeSecrets.ShouldBeTrue();
            service.LastCaller!.CanReadSecrets.ShouldBeTrue();
            return;
        }

        var exception = await act.ShouldThrowAsync<AccessDeniedException>();
        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe(NoSecretRights);
        service.Calls.ShouldBeEmpty();
    }

    // Without mTLS the project withholds both secret rights, but a refusal names mTLS only for the right the machine has:
    // with Secrets.Read alone the reads of a secret (reveal, version, diff, export) are MtlsRequired and the writes (set,
    // protect) AccessDenied, which mTLS would not change; with Secrets.Write alone the other way round.
    [Theory]
    [InlineData(MachineScope.SecretsRead, ErrorCodes.MtlsRequired, ErrorCodes.AccessDenied)]
    [InlineData(MachineScope.SecretsWrite, ErrorCodes.AccessDenied, ErrorCodes.MtlsRequired)]
    public async Task Secrets_ByAMachineWithOneSecretsScopeWithoutMtls_AreMtlsRequiredOnlyInTheDirectionOfTheScope(MachineScope secrets, string readError, string writeError)
    {
        MachineScope[] scopes = [MachineScope.ConfigurationRead, MachineScope.ConfigurationWrite, MachineScope.ConfigurationTransfer, MachineScope.KeyValueManage, secrets];
        var (http, service) = Create(AccountRole.None, requireMtls: true, scopes: scopes);
        service.Pair = RecordingKeyValueService.Secret("db.password", "s3cret");
        var secretWrite = FunctionTestDoubles.ToJsonBody(new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "s3cret" });
        var protect = FunctionTestDoubles.ToJsonBody(new KeyValueBulkRequest { Keys = ["a"], Protection = KeyValueProtection.Hsm });
        var export = FunctionTestDoubles.ToJsonBody(new KeyValueExportRequest { IncludeSecrets = true });

        AccessDeniedException[] reads =
        [
            await Should.ThrowAsync<AccessDeniedException>(() => http.GetKeyValue(MachineWithoutMtls(scopes, query: q => q["reveal"] = "true"), Org, Project, View, "db.password")),
            await Should.ThrowAsync<AccessDeniedException>(() => http.GetKeyValueVersion(MachineWithoutMtls(scopes, query: q => q["reveal"] = "true"), Org, Project, View, "db.password", 2)),
            await Should.ThrowAsync<AccessDeniedException>(() => http.GetKeyValueVersionDiff(MachineWithoutMtls(scopes), Org, Project, View, "db.password", 2)),
            await Should.ThrowAsync<AccessDeniedException>(() => http.ExportKeyValues(MachineWithoutMtls(scopes, "POST", body: export), Org, Project, View)),
        ];
        AccessDeniedException[] writes =
        [
            await Should.ThrowAsync<AccessDeniedException>(() => http.SetKeyValue(MachineWithoutMtls(scopes, "PUT", body: secretWrite), Org, Project, View, "db.password")),
            await Should.ThrowAsync<AccessDeniedException>(() => http.ProtectKeyValues(MachineWithoutMtls(scopes, "POST", body: protect), Org, Project, View)),
        ];

        reads.Select(exception => exception.ErrorCode).ShouldBe([readError, readError, readError, readError]);
        writes.Select(exception => exception.ErrorCode).ShouldBe([writeError, writeError]);
    }

    // A user's secret rights come from ContributorWithSecrets alone: a Contributor whose token carries Secrets.Read and
    // Secrets.Write reveals, exports, writes and protects no secret. Debug builds (DEV_AUTH) skip the role checks of
    // users and give every user the rights.
    [Fact]
    public async Task Secrets_ByAContributorWhoseTokenCarriesTheSecretsScopes_AreRefused()
    {
        if (DevAuth)
        {
            return;
        }

        var (http, service) = Create(AccountRole.Contributor);
        service.Pair = RecordingKeyValueService.Secret("db.password", "s3cret");

        var reveal = await Should.ThrowAsync<AccessDeniedException>(() => http.GetKeyValue(UserCarryingTheSecretsScopes(query: q => q["reveal"] = "true"), Org, Project, View, "db.password"));
        var export = await Should.ThrowAsync<AccessDeniedException>(() => http.ExportKeyValues(UserCarryingTheSecretsScopes("POST", body: FunctionTestDoubles.ToJsonBody(new KeyValueExportRequest { IncludeSecrets = true })), Org, Project, View));
        var set = await Should.ThrowAsync<AccessDeniedException>(() => http.SetKeyValue(UserCarryingTheSecretsScopes("PUT", body: FunctionTestDoubles.ToJsonBody(new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "s3cret" })), Org, Project, View, "db.password"));
        var protect = await Should.ThrowAsync<AccessDeniedException>(() => http.ProtectKeyValues(UserCarryingTheSecretsScopes("POST", body: FunctionTestDoubles.ToJsonBody(new KeyValueBulkRequest { Keys = ["a"], Protection = KeyValueProtection.Hsm })), Org, Project, View));

        reveal.Message.ShouldBe(NoRightsToTheSecret);
        export.Message.ShouldBe(NoSecretRights);
        set.Message.ShouldBe(NoRightsToTheSecret);
        protect.Message.ShouldBe(NoSecretRights);
        service.Calls.ShouldBe(["get:db.password", "set:db.password"], "the export and protect are refused before the service");
    }

    // ContributorWithSecrets gives a user both rights, with a token that carries only User.Impersonation.
    [Fact]
    public async Task Secrets_ByAContributorWithSecrets_NeedNoScope()
    {
        var (http, service) = Create(AccountRole.ContributorWithSecrets);
        service.Pair = RecordingKeyValueService.Secret("db.password", "s3cret");

        var reveal = await http.GetKeyValue(KeyValueRequests.User(query: q => q["reveal"] = "true"), Org, Project, View, "db.password");
        var export = await http.ExportKeyValues(KeyValueRequests.User("POST", body: FunctionTestDoubles.ToJsonBody(new KeyValueExportRequest { IncludeSecrets = true })), Org, Project, View);
        var set = await http.SetKeyValue(KeyValueRequests.User("PUT", body: FunctionTestDoubles.ToJsonBody(new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "s3cret" })), Org, Project, View, "db.password");
        var protect = await http.ProtectKeyValues(KeyValueRequests.User("POST", body: FunctionTestDoubles.ToJsonBody(new KeyValueBulkRequest { Keys = ["a"], Protection = KeyValueProtection.Hsm })), Org, Project, View);

        reveal.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<KeyValue>().Value.ShouldBe("s3cret");
        export.ShouldBeOfType<OkObjectResult>();
        set.ShouldBeOfType<OkObjectResult>();
        protect.ShouldBeOfType<OkObjectResult>();
        service.Calls.ShouldBe(["get:db.password", "export", "set:db.password", "protect:a"]);
        service.LastCaller!.CanReadSecrets.ShouldBeTrue();
        service.LastCaller.CanWriteSecrets.ShouldBeTrue();
    }

    [Fact]
    public async Task Import_PassesTheModeTheDryRunAndThePrefix()
    {
        var (http, service) = Create(AccountRole.Contributor);
        var document = new KeyValueDocument { Items = [new KeyValueDocumentItem { Key = "a", Protection = KeyValueProtection.Public, Value = "1" }] };

        var result = await http.ImportKeyValues(KeyValueRequests.User("POST", query: q => { q["mode"] = "replace"; q["dryRun"] = "true"; q["keyPrefix"] = "db."; }, body: FunctionTestDoubles.ToJsonBody(document)), Org, Project, View);
        var noBody = await http.ImportKeyValues(KeyValueRequests.User("POST"), Org, Project, View);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<KeyValueImportResult>().IsDryRun.ShouldBeTrue();
        service.LastImport.ShouldBe(new KeyValueImportOptions { Mode = KeyValueImportMode.Replace, DryRun = true, KeyPrefix = "db." });
        noBody.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.ImportInvalid);
    }

    // The worker's [FromBody] binding failed these bodies with a 500 before the handler ran. The value is required,
    // so {} is not a write either.
    [Theory]
    [InlineData("", "A body with Value is required.")]
    [InlineData("null", "A body with Value is required.")]
    [InlineData("[1, 2]", "The request body is not a key/value write (at $).")]
    [InlineData("{}", "The request body is not a key/value write (at $).")]
    public async Task SetKeyValue_MissingOrInvalidBody_IsBadRequest(string body, string message)
    {
        var (http, service) = Create(AccountRole.Contributor);

        var result = await http.SetKeyValue(KeyValueRequests.User("PUT", body: body), Org, Project, View, "k");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe(message);
        error.ErrorCode.ShouldBeNull();
        service.Calls.ShouldBeEmpty();
    }

    // The worker's [FromBody] binding refused a body without a content type; the handler reads it.
    [Fact]
    public async Task SetKeyValue_BodyWithoutAContentType_IsRead()
    {
        var (http, service) = Create(AccountRole.Contributor);
        var request = KeyValueRequests.User("PUT", body: FunctionTestDoubles.ToJsonBody(new KeyValueWrite { Protection = KeyValueProtection.Public, Value = "plain" }));
        request.Headers.Remove("Content-Type").ShouldBeTrue();

        var result = await http.SetKeyValue(request, Org, Project, View, "db.host");

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<KeyValue>().Value.ShouldBe("plain");
        service.Calls.ShouldBe(["set:db.host"]);
    }

    [Fact]
    public async Task SetKeyValue_PropertyAndEnumNamesInAnyCase_AreRead()
    {
        var (http, service) = Create(AccountRole.ContributorWithSecrets);
        const string body = """{ "value": "s3cret", "PROTECTION": "standard", }""";

        var result = await http.SetKeyValue(KeyValueRequests.User("PUT", body: body), Org, Project, View, "db.password");

        var pair = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<KeyValue>();
        pair.Protection.ShouldBe(KeyValueProtection.Standard);
        pair.Value.ShouldBeNull();
        service.Calls.ShouldBe(["set:db.password"]);
    }

    // {} is a bulk request without keys. Protect checks the secret rights after the body, so a contributor without
    // them gets the 400 too.
    [Theory]
    [InlineData("", "A body with Keys or a KeyPrefix is required.")]
    [InlineData("null", "A body with Keys or a KeyPrefix is required.")]
    [InlineData("{}", "A body with Keys or a KeyPrefix is required.")]
    [InlineData("[1, 2]", "The request body is not a key/value bulk request (at $).")]
    public async Task Bulk_MissingOrInvalidBody_IsBadRequest(string body, string message)
    {
        var (http, service) = Create(AccountRole.Contributor);
        var (administratorHttp, administratorService) = Create(AccountRole.Administrator);

        var restored = await http.RestoreKeyValues(KeyValueRequests.User("POST", body: body), Org, Project, View);
        var protectedResult = await http.ProtectKeyValues(KeyValueRequests.User("POST", body: body), Org, Project, View);
        var purged = await administratorHttp.PurgeKeyValues(KeyValueRequests.User("POST", body: body), Org, Project, View);

        foreach (var result in new[] { restored, protectedResult, purged })
        {
            var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
            error.Message.ShouldBe(message);
            error.ErrorCode.ShouldBeNull();
        }

        service.Calls.ShouldBeEmpty();
        administratorService.Calls.ShouldBeEmpty();
    }

    // The body of an export is optional: without one, the whole view exports without secret values.
    [Theory]
    [InlineData("")]
    [InlineData("null")]
    public async Task Export_WithoutABody_ExportsWithoutSecrets(string body)
    {
        var (http, service) = Create(AccountRole.Reader);

        var result = await http.ExportKeyValues(KeyValueRequests.User("POST", body: body), Org, Project, View);

        result.ShouldBeOfType<OkObjectResult>();
        service.LastExport.ShouldBe(new KeyValueExportRequest());
        service.Calls.ShouldBe(["export"]);
    }

    [Fact]
    public async Task Export_InvalidBody_IsBadRequest()
    {
        var (http, service) = Create(AccountRole.Reader);

        var result = await http.ExportKeyValues(KeyValueRequests.User("POST", body: "[1, 2]"), Org, Project, View);

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe("The request body is not a key/value export request (at $).");
        error.ErrorCode.ShouldBeNull();
        service.Calls.ShouldBeEmpty();
    }

    // The items are required, so {} is not a document either.
    [Theory]
    [InlineData("", "A document with Items is required.")]
    [InlineData("null", "A document with Items is required.")]
    [InlineData("[1, 2]", "The request body is not a key/value document (at $).")]
    [InlineData("{}", "The request body is not a key/value document (at $).")]
    public async Task Import_MissingOrInvalidBody_IsImportInvalid(string body, string message)
    {
        var (http, service) = Create(AccountRole.Contributor);

        var result = await http.ImportKeyValues(KeyValueRequests.User("POST", body: body), Org, Project, View);

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe(message);
        error.ErrorCode.ShouldBe(ErrorCodes.ImportInvalid);
        service.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task List_ParsesTheFilters()
    {
        var (http, service) = Create(AccountRole.Reader);

        await http.GetKeyValues(
            KeyValueRequests.User(query: q =>
            {
                q.Add("protection", "Public,standard");
                q.Add("protection", "Hyok");
                q["state"] = "all";
                q["includeValues"] = "true";
                q["keyPrefix"] = "db.";
                q["continuationToken"] = "next";
            }),
            Org,
            Project,
            View);

        service.LastList.ShouldNotBeNull();
        service.LastList.Protections.ShouldBe([KeyValueProtection.Public, KeyValueProtection.Standard, KeyValueProtection.Hyok]);
        service.LastList.State.ShouldBe(KeyValueState.All);
        service.LastList.IncludeValues.ShouldBeTrue();
        service.LastList.KeyPrefix.ShouldBe("db.");
        service.LastList.ContinuationToken.ShouldBe("next");
    }

    [Theory]
    [InlineData("state", "everything")]
    [InlineData("protection", "Secret")]
    [InlineData("includeValues", "yes")]
    public async Task List_WithABadQueryValue_IsInvalidQuery(string name, string value)
    {
        var (http, _) = Create(AccountRole.Reader);

        var act = () => http.GetKeyValues(KeyValueRequests.User(query: q => q[name] = value), Org, Project, View);

        (await act.ShouldThrowAsync<InvalidInputException>()).ErrorCode.ShouldBe(ErrorCodes.InvalidQuery);
    }

    [Fact]
    public async Task Versions_EmptyIsNotFound_AndTheDiffUsesThePreviousVersion()
    {
        var (http, service) = Create(AccountRole.Reader);

        var missing = await http.GetKeyValueVersions(KeyValueRequests.User(), Org, Project, View, "k");
        service.Versions = [new KeyValueVersion { Version = 1, Protection = KeyValueProtection.Public, Modified = AuditStamp.Of(AuditActor.Account("a"), DateTimeOffset.UnixEpoch), IsCurrent = true }];
        var found = await http.GetKeyValueVersions(KeyValueRequests.User(), Org, Project, View, "k");
        await http.GetKeyValueVersionDiff(KeyValueRequests.User(), Org, Project, View, "k", 3);
        var first = service.LastDiff;
        await http.GetKeyValueVersionDiff(KeyValueRequests.User(), Org, Project, View, "k", 1);
        var second = service.LastDiff;
        await http.GetKeyValueVersionDiffCustom(KeyValueRequests.User(), Org, Project, View, "k", 7, 2);

        missing.ShouldBeOfType<NotFoundObjectResult>();
        found.ShouldBeOfType<OkObjectResult>();
        first.ShouldBe((2UL, 3UL));
        second.ShouldBe((0UL, 1UL));
        service.LastDiff.ShouldBe((2UL, 7UL));
    }

    [Fact]
    public async Task Diff_OfASecretByAMachineWithoutMtls_IsMtlsRequired()
    {
        var (http, service) = Create(AccountRole.None, requireMtls: true);
        service.Pair = RecordingKeyValueService.Secret("k", "s");

        var act = () => http.GetKeyValueVersionDiff(KeyValueRequests.Machine(mtls: false), Org, Project, View, "k", 2);

        (await act.ShouldThrowAsync<AccessDeniedException>()).ErrorCode.ShouldBe(ErrorCodes.MtlsRequired);
    }

    [Fact]
    public async Task GetKeyValueVersion_RevealsOnlyWhenAsked()
    {
        var (http, service) = Create(AccountRole.ContributorWithSecrets);
        service.Pair = RecordingKeyValueService.Secret("k", "s");

        var hidden = await http.GetKeyValueVersion(KeyValueRequests.User(), Org, Project, View, "k", 2);
        var revealed = await http.GetKeyValueVersion(KeyValueRequests.User(query: q => q["reveal"] = "true"), Org, Project, View, "k", 2);
        service.Pair = null;
        var missing = await http.GetKeyValueVersion(KeyValueRequests.User(), Org, Project, View, "k", 9);

        hidden.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<KeyValue>().Value.ShouldBeNull();
        revealed.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<KeyValue>().Value.ShouldBe("s");
        missing.ShouldBeOfType<NotFoundObjectResult>().Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.KeyValueNotFound);
    }

    [Fact]
    public async Task Policy_ReadersAndMachinesRead_OnlyUsersChange()
    {
        var policies = new RecordingPolicyService();
        var http = new KeyValuePolicyHttp(policies, new FixedKeyValuePolicies(), new RoleAccess(AccountRole.Administrator), FunctionTestDoubles.ApiJsonOptions);

        var byMachine = await http.GetKeyValuePolicy(KeyValueRequests.Machine(mtls: false, roles: [Scopes.Configuration.Read]), Org, Project);
        var recipients = await http.GetRecipients(KeyValueRequests.User(), Org, Project);
        var updated = await http.SetKeyValuePolicy(KeyValueRequests.User("PUT", body: FunctionTestDoubles.ToJsonBody(new KeyValuePolicyUpdate { RequireMtlsForSecretDelivery = false })), Org, Project);
        var setup = await http.GetByokSetup(KeyValueRequests.User(), Org, Project);
        var act = () => http.SetKeyValuePolicy(KeyValueRequests.Machine(mtls: true, "PUT", body: FunctionTestDoubles.ToJsonBody(new KeyValuePolicyUpdate())), Org, Project);

        byMachine.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<KeyValuePolicy>();
        recipients.ShouldBeOfType<OkObjectResult>();
        updated.ShouldBeOfType<OkObjectResult>();
        setup.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<ByokSetupInfo>().ConsentUrl.ShouldContain("adminconsent");
        await act.ShouldThrowAsync<AccessDeniedException>();
        policies.Calls.ShouldBe(["get", "get", "update", "byok-setup"]);
        policies.LastCaller!.Actor.Id.ShouldBe("user-1");
    }

    [Fact]
    public async Task Policy_RotateVerifyAndRecipients()
    {
        var policies = new RecordingPolicyService();
        var http = new KeyValuePolicyHttp(policies, new FixedKeyValuePolicies(), new RoleAccess(AccountRole.Administrator), FunctionTestDoubles.ApiJsonOptions);

        var rotated = await http.RotateKeyValueKey(KeyValueRequests.User("POST", body: FunctionTestDoubles.ToJsonBody(new KeyValueRotateRequest { Protection = KeyValueProtection.Hsm })), Org, Project);
        var badRotate = await http.RotateKeyValueKey(KeyValueRequests.User("POST"), Org, Project);
        var verified = await http.VerifyByok(KeyValueRequests.User("POST"), Org, Project);
        var added = await http.AddRecipient(KeyValueRequests.User("POST", body: FunctionTestDoubles.ToJsonBody(new ClientEncryptionRecipientCreate { Name = "api", PublicKey = "-----BEGIN PUBLIC KEY-----" })), Org, Project);
        var badAdd = await http.AddRecipient(KeyValueRequests.User("POST", body: FunctionTestDoubles.ToJsonBody(new ClientEncryptionRecipientCreate { Name = string.Empty, PublicKey = "x" })), Org, Project);
        var renamed = await http.UpdateRecipient(KeyValueRequests.User("PATCH", body: FunctionTestDoubles.ToJsonBody(new ClientEncryptionRecipientUpdate { Name = "api2" })), Org, Project, "r1");
        var badUpdate = await http.UpdateRecipient(KeyValueRequests.User("PATCH", body: FunctionTestDoubles.ToJsonBody(new ClientEncryptionRecipientUpdate())), Org, Project, "r1");
        var removed = await http.RemoveRecipient(KeyValueRequests.User("DELETE"), Org, Project, "r1");

        rotated.ShouldBeOfType<OkObjectResult>();
        badRotate.ShouldBeOfType<BadRequestObjectResult>();
        verified.ShouldBeOfType<OkObjectResult>();
        added.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<ClientEncryptionRecipient>().Name.ShouldBe("api");
        badAdd.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.RecipientInvalid);
        renamed.ShouldBeOfType<OkObjectResult>();
        badUpdate.ShouldBeOfType<BadRequestObjectResult>();
        removed.ShouldBeOfType<NoContentResult>();
        policies.Calls.ShouldBe(["rotate:Hsm", "verify", "add:api", "update:r1", "remove:r1"]);
    }

    [Fact]
    public async Task Policy_ChangesByAContributor_AreRefused()
    {
        if (DevAuth)
        {
            return;
        }

        var http = new KeyValuePolicyHttp(new RecordingPolicyService(), new FixedKeyValuePolicies(), new RoleAccess(AccountRole.ContributorWithSecrets), FunctionTestDoubles.ApiJsonOptions);

        var act = () => http.SetKeyValuePolicy(KeyValueRequests.User("PUT", body: FunctionTestDoubles.ToJsonBody(new KeyValuePolicyUpdate())), Org, Project);

        await act.ShouldThrowAsync<AccessDeniedException>();
    }

    // {} is a policy update that changes nothing.
    [Theory]
    [InlineData("", "A body with the policy members to change is required.")]
    [InlineData("null", "A body with the policy members to change is required.")]
    [InlineData("[1, 2]", "The request body is not a key/value policy update (at $).")]
    public async Task SetKeyValuePolicy_MissingOrInvalidBody_IsBadRequest(string body, string message)
    {
        var policies = new RecordingPolicyService();
        var http = CreatePolicy(policies);

        var result = await http.SetKeyValuePolicy(KeyValueRequests.User("PUT", body: body), Org, Project);

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe(message);
        error.ErrorCode.ShouldBeNull();
        policies.Calls.ShouldBeEmpty();
    }

    // The protection is required, so {} is not a rotation request either.
    [Theory]
    [InlineData("", "A body with the Protection to rotate is required.")]
    [InlineData("null", "A body with the Protection to rotate is required.")]
    [InlineData("[1, 2]", "The request body is not a key rotation request (at $).")]
    [InlineData("{}", "The request body is not a key rotation request (at $).")]
    public async Task RotateKeyValueKey_MissingOrInvalidBody_IsBadRequest(string body, string message)
    {
        var policies = new RecordingPolicyService();
        var http = CreatePolicy(policies);

        var result = await http.RotateKeyValueKey(KeyValueRequests.User("POST", body: body), Org, Project);

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe(message);
        error.ErrorCode.ShouldBeNull();
        policies.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task RotateKeyValueKey_EnumNameInAnyCase_IsRead()
    {
        var policies = new RecordingPolicyService();
        var http = CreatePolicy(policies);

        var result = await http.RotateKeyValueKey(KeyValueRequests.User("POST", body: """{ "protection": "hsm" }"""), Org, Project);

        result.ShouldBeOfType<OkObjectResult>();
        policies.Calls.ShouldBe(["rotate:Hsm"]);
    }

    // The name and the public key are required, so {} is not a recipient either.
    [Theory]
    [InlineData("", "A body with Name and PublicKey is required.")]
    [InlineData("null", "A body with Name and PublicKey is required.")]
    [InlineData("[1, 2]", "The request body is not a client encryption recipient (at $).")]
    [InlineData("{}", "The request body is not a client encryption recipient (at $).")]
    public async Task AddRecipient_MissingOrInvalidBody_IsRecipientInvalid(string body, string message)
    {
        var policies = new RecordingPolicyService();
        var http = CreatePolicy(policies);

        var result = await http.AddRecipient(KeyValueRequests.User("POST", body: body), Org, Project);

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe(message);
        error.ErrorCode.ShouldBe(ErrorCodes.RecipientInvalid);
        policies.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", "A body with Name or IsEnabled is required.")]
    [InlineData("null", "A body with Name or IsEnabled is required.")]
    [InlineData("{}", "A body with Name or IsEnabled is required.")]
    [InlineData("[1, 2]", "The request body is not a client encryption recipient update (at $).")]
    public async Task UpdateRecipient_MissingOrInvalidBody_IsBadRequest(string body, string message)
    {
        var policies = new RecordingPolicyService();
        var http = CreatePolicy(policies);

        var result = await http.UpdateRecipient(KeyValueRequests.User("PATCH", body: body), Org, Project, "r1");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe(message);
        error.ErrorCode.ShouldBeNull();
        policies.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Audit_ParsesTheFilters_AndUsageDefaultsToTheLast30Days()
    {
        var audit = new RecordingAuditLog();
        var usage = new RecordingUsageService();
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
        var http = new KeyValueUsageHttp(audit, usage, new RoleAccess(AccountRole.Administrator), clock);

        var events = await http.GetKeyValueAudit(
            KeyValueRequests.User(query: q =>
            {
                q["view"] = "default";
                q["key"] = "db.password";
                q["operation"] = "reveal";
                q["from"] = "2026-10-01";
                q["to"] = "2026-10-13T00:00:00Z";
            }),
            Org,
            Project);
        var report = await http.GetKeyValueUsage(KeyValueRequests.User(), Org, Project);
        var ranged = await http.GetKeyValueUsage(KeyValueRequests.User(query: q => { q["from"] = "2026-09-01"; q["to"] = "2026-09-30"; }), Org, Project);
        var byMachine = () => http.GetKeyValueAudit(KeyValueRequests.Machine(mtls: true), Org, Project);

        events.ShouldBeOfType<OkObjectResult>();
        audit.LastQuery.ShouldBe(new KeyValueAuditQuery
        {
            View = "default",
            Key = "db.password",
            Operation = KeyValueAuditOperation.Reveal,
            From = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero),
        });
        report.ShouldBeOfType<OkObjectResult>();
        usage.LastRange.ShouldNotBeNull();
        ranged.ShouldBeOfType<OkObjectResult>();
        usage.LastRange.ShouldBe((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));
        await byMachine.ShouldThrowAsync<AccessDeniedException>();
    }

    [Fact]
    public async Task Usage_DefaultRange_EndsToday()
    {
        var usage = new RecordingUsageService();
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
        var http = new KeyValueUsageHttp(new RecordingAuditLog(), usage, new RoleAccess(AccountRole.Administrator), clock);

        await http.GetKeyValueUsage(KeyValueRequests.User(), Org, Project);

        usage.LastRange.ShouldBe((new DateOnly(2026, 9, 13), new DateOnly(2026, 10, 13)));
    }

    // The machine of the access double has the six runtime scopes and the given management scopes (none by default), or
    // exactly the given scopes.
    private static (KeyValuesHttp Http, RecordingKeyValueService Service) Create(
        AccountRole role,
        bool requireMtls = true,
        IReadOnlyList<MachineScope>? managementScopes = null,
        IReadOnlyList<MachineScope>? scopes = null)
    {
        var service = new RecordingKeyValueService();
        var http = new KeyValuesHttp(service, new RoleAccess(role, managementScopes: managementScopes, scopes: scopes), new FixedKeyValuePolicies(requireMtls), FunctionTestDoubles.ApiJsonOptions, NullLogger<KeyValuesHttp>.Instance);
        return (http, service);
    }

    private static KeyValuePolicyHttp CreatePolicy(RecordingPolicyService policies)
    {
        return new KeyValuePolicyHttp(policies, new FixedKeyValuePolicies(), new RoleAccess(AccountRole.Administrator), FunctionTestDoubles.ApiJsonOptions);
    }

    // The handlers over recording services, for a machine created with exactly the given scopes in a project that
    // delivers secrets to machines only over mTLS, or for users with the given role.
    private static KeyValueHandlers Handlers(IReadOnlyList<MachineScope> scopes, AccountRole role = AccountRole.None)
    {
        return new KeyValueHandlers(new RoleAccess(role, scopes: scopes));
    }

    private static ScopedHandler Scoped(string name)
    {
        return MachineHandlers.Single(handler => handler.Name == name);
    }

    private static Handler Administrator(string name)
    {
        return AdministratorHandlers.Single(handler => handler.Name == name);
    }

    // The scopes but the excluded ones; a null excludes nothing.
    private static MachineScope[] Without(IEnumerable<MachineScope> scopes, params MachineScope?[] excluded)
    {
        return [.. scopes.Where(scope => !excluded.Contains(scope))];
    }

    // The request of a row by a machine whose credentials carry the given scopes.
    private static TestHttpRequestData Request(ScopedHandler handler, IEnumerable<MachineScope> carried, bool mtls)
    {
        return TestRequests.Machine(carried, handler.Method, handler.Body, mtls: mtls);
    }

    // A machine with an mTLS certificate whose credentials carry the given scopes.
    private static TestHttpRequestData MachineOverMtls(IEnumerable<MachineScope> carried, string method = "GET", Action<NameValueCollection>? query = null, string? body = null)
    {
        return MachineCarrying(carried, mtls: true, method, query, body);
    }

    // The same machine without a certificate.
    private static TestHttpRequestData MachineWithoutMtls(IEnumerable<MachineScope> carried, string method = "GET", Action<NameValueCollection>? query = null, string? body = null)
    {
        return MachineCarrying(carried, mtls: false, method, query, body);
    }

    private static TestHttpRequestData MachineCarrying(IEnumerable<MachineScope> carried, bool mtls, string method, Action<NameValueCollection>? query, string? body)
    {
        var request = TestRequests.Machine(carried, method, body, mtls: mtls);
        query?.Invoke(request.Query);
        return request;
    }

    // A user token that carries the secrets scopes beside User.Impersonation; they give a user nothing.
    private static TestHttpRequestData UserCarryingTheSecretsScopes(string method = "GET", Action<NameValueCollection>? query = null, string? body = null)
    {
        var request = TestRequests.UserCarrying([Scopes.User.Impersonation, Scopes.Secrets.Read, Scopes.Secrets.Write], method, body);
        query?.Invoke(request.Query);
        return request;
    }

    private static TestHttpRequestData Permanent(TestHttpRequestData request)
    {
        request.Query["permanent"] = "true";
        return request;
    }

    // The 403 of a machine scope that is not stored on the machine or not carried by its credentials, before any service.
    private static async Task ShouldNeedTheScopeAsync(KeyValueHandlers http, Func<Task<IActionResult>> act, MachineScope scope, bool isStored)
    {
        var exception = await Should.ThrowAsync<AccessDeniedException>(act);

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe(isStored
            ? $"This operation needs the scope {MachineScopes.ToScopeString(scope)}, which the credentials of the machine do not carry."
            : $"This operation needs the scope {MachineScopes.ToScopeString(scope)}, which the machine was not created with.");
        http.Calls.ShouldBeEmpty();
    }

    // A handler: its HTTP method, a valid body, and how it is called with a request.
    private sealed record Handler(string Name, string Method, string? Body, Func<KeyValueHandlers, TestHttpRequestData, Task<IActionResult>> Invoke);

    // A handler a machine calls, with its machine scopes: the runtime scope, the management scope of a management
    // operation, and the secrets scope it needs on top.
    private sealed record ScopedHandler(
        string Name,
        string Method,
        string? Body,
        Func<KeyValueHandlers, TestHttpRequestData, Task<IActionResult>> Invoke,
        MachineScope Scope,
        MachineScope? ManagementScope = null,
        MachineScope? SecretsScope = null)
    {
        // Every scope of the row, in the order they are checked.
        public MachineScope[] Scopes => [.. new[] { Scope, ManagementScope, SecretsScope }.OfType<MachineScope>()];
    }

    // The key/value, policy and usage handlers over recording services and one access double.
    private sealed class KeyValueHandlers
    {
        public KeyValueHandlers(IAccessService access)
        {
            var policies = new FixedKeyValuePolicies(requireMtls: true);
            KeyValues = new KeyValuesHttp(Service, access, policies, FunctionTestDoubles.ApiJsonOptions, NullLogger<KeyValuesHttp>.Instance);
            Policy = new KeyValuePolicyHttp(Policies, policies, access, FunctionTestDoubles.ApiJsonOptions);
            Usage = new KeyValueUsageHttp(Audit, UsageReports, access, new FixedClock(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero)));
        }

        public RecordingKeyValueService Service { get; } = new();

        public RecordingPolicyService Policies { get; } = new();

        public RecordingAuditLog Audit { get; } = new();

        public RecordingUsageService UsageReports { get; } = new();

        public KeyValuesHttp KeyValues { get; }

        public KeyValuePolicyHttp Policy { get; }

        public KeyValueUsageHttp Usage { get; }

        // Every call that reached a service.
        public IReadOnlyList<string> Calls =>
        [
            .. Service.Calls,
            .. Policies.Calls,
            .. Audit.LastQuery is null ? Array.Empty<string>() : ["audit"],
            .. UsageReports.LastRange is null ? Array.Empty<string>() : ["usage"],
        ];
    }
}
