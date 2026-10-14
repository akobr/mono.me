using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;
using _42.Platform.Storyteller.KeyValues;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The key/value endpoints: who the caller is, the mTLS rule, the query parsing, and what the responses carry.
// Role and scope refusals (403 from CheckAccessToProjectAsync, 401 from CheckScope) are skipped in a Debug build (DEV_AUTH).
public class KeyValuesHttpTests
{
    private const string Org = "acme";
    private const string Project = "billing";
    private const string View = "default";

    private static bool DevAuth => HttpRequestDataExtensions.IsDevelopmentAuthentication;

    [Fact]
    public async Task GetKeyValue_RevealByAUserWithSecrets_PassesACallerWithRights()
    {
        var (http, service) = Create(AccountRole.ContributorWithSecrets);
        service.Pair = RecordingKeyValueService.Secret("db.password", "s3cret");

        var result = await http.GetKeyValue(KeyValueRequests.User(secretsScope: true, query: q => q["reveal"] = "true"), Org, Project, View, "db.password");

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

        var secret = await http.SetKeyValue(KeyValueRequests.User("PUT", secretsScope: true), new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "s3cret" }, Org, Project, View, "db.password");
        var @public = await http.SetKeyValue(KeyValueRequests.User("PUT"), new KeyValueWrite { Protection = KeyValueProtection.Public, Value = "plain" }, Org, Project, View, "db.host");

        secret.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<KeyValue>().Value.ShouldBeNull();
        @public.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<KeyValue>().Value.ShouldBe("plain");
    }

    [Fact]
    public async Task SetKeyValue_WithoutABody_IsBadRequest()
    {
        var (http, service) = Create(AccountRole.Contributor);

        var result = await http.SetKeyValue(KeyValueRequests.User("PUT"), null, Org, Project, View, "k");

        result.ShouldBeOfType<BadRequestObjectResult>();
        service.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetKeyValue_SecretByAMachineWithoutMtls_TurnsTheRefusalIntoMtlsRequired()
    {
        var (http, _) = Create(AccountRole.None, requireMtls: true);

        var act = () => http.SetKeyValue(KeyValueRequests.Machine(mtls: false, "PUT"), new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "s" }, Org, Project, View, "db.password");

        (await act.ShouldThrowAsync<AccessDeniedException>()).ErrorCode.ShouldBe(ErrorCodes.MtlsRequired);
    }

    [Fact]
    public async Task SetKeyValue_SecretByAMachineWithoutTheScope_StaysAccessDenied()
    {
        if (DevAuth)
        {
            return;
        }

        var (http, _) = Create(AccountRole.None, requireMtls: false);

        var act = () => http.SetKeyValue(KeyValueRequests.Machine(mtls: true, "PUT", roles: [Scopes.Configuration.Write]), new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "s" }, Org, Project, View, "db.password");

        (await act.ShouldThrowAsync<AccessDeniedException>()).ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
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
    public async Task DeleteKeyValue_PermanentByAMachine_IsRefused()
    {
        var (http, service) = Create(AccountRole.None);

        var act = () => http.DeleteKeyValue(KeyValueRequests.Machine(mtls: true, "DELETE", query: q => q["permanent"] = "true"), Org, Project, View, "k");

        await act.ShouldThrowAsync<AccessDeniedException>();
        service.Calls.ShouldBeEmpty();
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

        var noKeys = await http.RestoreKeyValues(KeyValueRequests.User("POST"), new KeyValueBulkRequest(), Org, Project, View);
        var noLevel = await http.ProtectKeyValues(KeyValueRequests.User("POST", secretsScope: true), new KeyValueBulkRequest { Keys = ["a"] }, Org, Project, View);
        var restored = await http.RestoreKeyValues(KeyValueRequests.User("POST"), new KeyValueBulkRequest { KeyPrefix = "db." }, Org, Project, View);
        var protectedResult = await http.ProtectKeyValues(KeyValueRequests.User("POST", secretsScope: true), new KeyValueBulkRequest { Keys = ["a", "b"], Protection = KeyValueProtection.Hsm }, Org, Project, View);

        noKeys.ShouldBeOfType<BadRequestObjectResult>();
        noLevel.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.ProtectionRequired);
        restored.ShouldBeOfType<OkObjectResult>();
        protectedResult.ShouldBeOfType<OkObjectResult>();
        service.Calls.ShouldBe(["restore:db.", "protect:a|b"]);
    }

    [Fact]
    public async Task Protect_ByAMachineWithoutMtls_IsMtlsRequired()
    {
        var (http, _) = Create(AccountRole.None, requireMtls: true);

        var act = () => http.ProtectKeyValues(KeyValueRequests.Machine(mtls: false, "POST"), new KeyValueBulkRequest { Keys = ["a"], Protection = KeyValueProtection.Hsm }, Org, Project, View);

        (await act.ShouldThrowAsync<AccessDeniedException>()).ErrorCode.ShouldBe(ErrorCodes.MtlsRequired);
    }

    [Fact]
    public async Task Purge_ByAMachine_IsRefused()
    {
        var (http, service) = Create(AccountRole.None);

        var act = () => http.PurgeKeyValues(KeyValueRequests.Machine(mtls: true, "POST"), new KeyValueBulkRequest { Keys = ["a"] }, Org, Project, View);

        await act.ShouldThrowAsync<AccessDeniedException>();
        service.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Export_WithSecrets_NeedsTheRights()
    {
        var (http, service) = Create(AccountRole.None, requireMtls: true);

        var withoutSecrets = await http.ExportKeyValues(KeyValueRequests.Machine(mtls: false, "POST"), null, Org, Project, View);
        var withMtls = await http.ExportKeyValues(KeyValueRequests.Machine(mtls: true, "POST"), new KeyValueExportRequest { IncludeSecrets = true }, Org, Project, View);
        var act = () => http.ExportKeyValues(KeyValueRequests.Machine(mtls: false, "POST"), new KeyValueExportRequest { IncludeSecrets = true }, Org, Project, View);

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

        var act = () => http.ExportKeyValues(KeyValueRequests.User("POST"), new KeyValueExportRequest { IncludeSecrets = true }, Org, Project, View);

        (await act.ShouldThrowAsync<AccessDeniedException>()).ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
    }

    [Fact]
    public async Task Import_PassesTheModeTheDryRunAndThePrefix()
    {
        var (http, service) = Create(AccountRole.Contributor);
        var document = new KeyValueDocument { Items = [new KeyValueDocumentItem { Key = "a", Protection = KeyValueProtection.Public, Value = "1" }] };

        var result = await http.ImportKeyValues(KeyValueRequests.User("POST", query: q => { q["mode"] = "replace"; q["dryRun"] = "true"; q["keyPrefix"] = "db."; }), document, Org, Project, View);
        var noBody = await http.ImportKeyValues(KeyValueRequests.User("POST"), null, Org, Project, View);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<KeyValueImportResult>().IsDryRun.ShouldBeTrue();
        service.LastImport.ShouldBe(new KeyValueImportOptions { Mode = KeyValueImportMode.Replace, DryRun = true, KeyPrefix = "db." });
        noBody.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.ImportInvalid);
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

        var hidden = await http.GetKeyValueVersion(KeyValueRequests.User(secretsScope: true), Org, Project, View, "k", 2);
        var revealed = await http.GetKeyValueVersion(KeyValueRequests.User(secretsScope: true, query: q => q["reveal"] = "true"), Org, Project, View, "k", 2);
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
        var http = new KeyValuePolicyHttp(policies, new FixedKeyValuePolicies(), new RoleAccess(AccountRole.Administrator));

        var byMachine = await http.GetKeyValuePolicy(KeyValueRequests.Machine(mtls: false, roles: [Scopes.Configuration.Read]), Org, Project);
        var recipients = await http.GetRecipients(KeyValueRequests.User(), Org, Project);
        var updated = await http.SetKeyValuePolicy(KeyValueRequests.User("PUT"), new KeyValuePolicyUpdate { RequireMtlsForSecretDelivery = false }, Org, Project);
        var setup = await http.GetByokSetup(KeyValueRequests.User(), Org, Project);
        var act = () => http.SetKeyValuePolicy(KeyValueRequests.Machine(mtls: true, "PUT"), new KeyValuePolicyUpdate(), Org, Project);

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
        var http = new KeyValuePolicyHttp(policies, new FixedKeyValuePolicies(), new RoleAccess(AccountRole.Administrator));

        var rotated = await http.RotateKeyValueKey(KeyValueRequests.User("POST"), new KeyValueRotateRequest { Protection = KeyValueProtection.Hsm }, Org, Project);
        var badRotate = await http.RotateKeyValueKey(KeyValueRequests.User("POST"), null, Org, Project);
        var verified = await http.VerifyByok(KeyValueRequests.User("POST"), Org, Project);
        var added = await http.AddRecipient(KeyValueRequests.User("POST"), new ClientEncryptionRecipientCreate { Name = "api", PublicKey = "-----BEGIN PUBLIC KEY-----" }, Org, Project);
        var badAdd = await http.AddRecipient(KeyValueRequests.User("POST"), new ClientEncryptionRecipientCreate { Name = string.Empty, PublicKey = "x" }, Org, Project);
        var renamed = await http.UpdateRecipient(KeyValueRequests.User("PATCH"), new ClientEncryptionRecipientUpdate { Name = "api2" }, Org, Project, "r1");
        var badUpdate = await http.UpdateRecipient(KeyValueRequests.User("PATCH"), new ClientEncryptionRecipientUpdate(), Org, Project, "r1");
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

        var http = new KeyValuePolicyHttp(new RecordingPolicyService(), new FixedKeyValuePolicies(), new RoleAccess(AccountRole.ContributorWithSecrets));

        var act = () => http.SetKeyValuePolicy(KeyValueRequests.User("PUT"), new KeyValuePolicyUpdate(), Org, Project);

        await act.ShouldThrowAsync<AccessDeniedException>();
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

    private static (KeyValuesHttp Http, RecordingKeyValueService Service) Create(AccountRole role, bool requireMtls = true)
    {
        var service = new RecordingKeyValueService();
        var http = new KeyValuesHttp(service, new RoleAccess(role), new FixedKeyValuePolicies(requireMtls), NullLogger<KeyValuesHttp>.Instance);
        return (http, service);
    }
}
