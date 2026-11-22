using System.Reflection;

using _42.Platform.Storyteller.Api.V1;

using Microsoft.Azure.Functions.Worker;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests.TenantIsolation;

// Every HTTP function of the API, in its family. TenantRoutesTests keeps the table complete: a new function fails them
// until it is classified here.
public static class TenantRoutes
{
    public const string AnnotationKey = "sbt.sample";

    // A subject of the route's key; the handler reads the type by its exact member name.
    private const string JsonPatch = """[{"op":"add","path":"/sample","value":1}]""";

    private const string AnnotationBody = $$"""{"AnnotationType":"Subject","annotationKey":"{{AnnotationKey}}","name":"sample","projectName":"app","viewName":"default"}""";

    public static IReadOnlyList<TenantRoute> All { get; } =
    [
        // Project level: v1/{organization}/{project}/...
        Api(nameof(ProjectSettingsHttp.GetProjectSettings), "project-settings"),
        Api(nameof(ProjectSettingsHttp.SetProjectSettings), "project-settings", usersOnly: true, body: "{}"),
        Api(nameof(ProjectSettingsHttp.PatchProjectSettings), "project-settings", usersOnly: true, body: "{}"),
        Api(nameof(ViewsHttp.GetViews), "views"),
        Api(nameof(ViewsHttp.CreateView), "views", usersOnly: true, body: """{"name":"other"}"""),
        Api(nameof(ViewsHttp.UpdateView), "views", usersOnly: true, body: "{}"),
        Api(nameof(BindingsHttp.GetBindingCatalog), "bindings"),
        Api(nameof(AccessHttp.GetMachines), "machines", usersOnly: true),
        Api(nameof(AccessHttp.PostMachines), "machines", usersOnly: true, body: """{"name":"m","scopes":["ConfigurationRead"]}"""),
        Api(nameof(AccessHttp.GetMachine), "machines", usersOnly: true),
        Api(nameof(AccessHttp.PutMachine), "machines", usersOnly: true),
        Api(nameof(AccessHttp.DeleteMachine), "machines", usersOnly: true),
        new(nameof(CertificatesHttp.PostRenewCertificate), "machine-certificate-renew", TenantGate.Service, TenantAddress.Route),
        Api(nameof(FederationHttp.PutMachineFederationRules), "machine-federation-rules", usersOnly: true, body: "[]"),
        Api(nameof(FederationHttp.GetFederatedIssuers), "federated-issuers", usersOnly: true),
        Api(nameof(FederationHttp.GetFederatedIssuer), "federated-issuers", usersOnly: true),
        Api(nameof(FederationHttp.PutFederatedIssuer), "federated-issuers", usersOnly: true, body: """{"issuer":"https://token.actions.githubusercontent.com"}"""),
        Api(nameof(FederationHttp.DeleteFederatedIssuer), "federated-issuers", usersOnly: true),
        new(nameof(FederationHttp.PostMachineToken), "machine-token", TenantGate.Service, TenantAddress.Route)
        {
            Body = $"grant_type={Uri.EscapeDataString("urn:ietf:params:oauth:grant-type:token-exchange")}&subject_token=eyJhbGciOiJSUzI1NiJ9.eyJpc3MiOiJ4In0.c2ln&subject_token_type={Uri.EscapeDataString("urn:ietf:params:oauth:token-type:jwt")}&client_id={TenantWorld.FederatedMachineId}",
            ContentType = "application/x-www-form-urlencoded",
        },
        Api(nameof(CertificatesHttp.GetSharedCertificates), "shared-certificates", usersOnly: true),
        Api(nameof(CertificatesHttp.PostSharedCertificate), "shared-certificates", usersOnly: true, body: """{"label":"shared"}"""),
        Api(nameof(CertificatesHttp.DeleteSharedCertificate), "shared-certificates", usersOnly: true),
        Api(nameof(KeyValuePolicyHttp.GetKeyValuePolicy), "key-value-policy"),
        Api(nameof(KeyValuePolicyHttp.SetKeyValuePolicy), "key-value-policy", usersOnly: true, body: "{}"),
        Api(nameof(KeyValuePolicyHttp.GetByokSetup), "key-value-policy", usersOnly: true),
        Api(nameof(KeyValuePolicyHttp.VerifyByok), "key-value-policy", usersOnly: true, body: "{}"),
        Api(nameof(KeyValuePolicyHttp.RotateKeyValueKey), "key-value-policy", usersOnly: true, body: "{}"),
        Api(nameof(KeyValuePolicyHttp.GetRecipients), "key-value-recipients"),
        Api(nameof(KeyValuePolicyHttp.AddRecipient), "key-value-recipients", usersOnly: true, body: "{}"),
        Api(nameof(KeyValuePolicyHttp.UpdateRecipient), "key-value-recipients", usersOnly: true, body: "{}"),
        Api(nameof(KeyValuePolicyHttp.RemoveRecipient), "key-value-recipients", usersOnly: true),
        Api(nameof(KeyValueUsageHttp.GetKeyValueAudit), "key-value-audit", usersOnly: true),
        Api(nameof(KeyValueUsageHttp.GetKeyValueUsage), "key-value-usage", usersOnly: true),

        // View level: v1/{organization}/{project}/{view}/...
        Api(nameof(AnnotationsHttp.GetAnnotations), "annotations"),
        Api(nameof(AnnotationsHttp.SetAnnotations), "annotations", body: $$"""[{"annotationKey":"{{AnnotationKey}}","name":"sample","annotationType":"Subject","projectName":"app","viewName":"default"}]"""),
        Api(nameof(AnnotationsHttp.SetAnnotationsSimple), "annotations", body: "[]"),
        Api(nameof(AnnotationsHttp.GetAnnotation), "annotation"),
        Api(nameof(AnnotationsHttp.SetAnnotation), "annotation", body: AnnotationBody),
        Api(nameof(AnnotationsHttp.PatchAnnotation), "annotation", body: "{}"),
        Api(nameof(AnnotationsHttp.DeleteAnnotation), "annotation"),
        Api(nameof(AnnotationsHttp.GetDescendants), "annotation"),
        Api(nameof(AnnotationsHttp.GetAnnotationHistory), "annotation"),
        Api(nameof(AnnotationsHttp.GetDeletedAnnotations), "deleted-annotations"),
        Api(nameof(AnnotationsHttp.GetDeletedAnnotation), "deleted-annotations"),
        Api(nameof(AnnotationsHttp.FindDeletedAnnotations), "deleted-annotations", body: $$"""{"keys":["{{AnnotationKey}}"]}"""),
        Api(nameof(AnnotationsHttp.RestoreAnnotations), "deleted-annotations", body: $$"""{"keys":["{{AnnotationKey}}"]}"""),
        Api(nameof(AnnotationsHttp.PurgeAnnotation), "deleted-annotations", usersOnly: true),
        Api(nameof(AnnotationsHttp.GetResponsibilities), "annotation-lists"),
        Api(nameof(AnnotationsHttp.GetSubjects), "annotation-lists"),
        Api(nameof(AnnotationsHttp.GetUsages), "annotation-lists"),
        Api(nameof(AnnotationsHttp.GetContexts), "annotation-lists"),
        Api(nameof(AnnotationsHttp.GetExecutions), "annotation-lists"),
        Api(nameof(AnnotationsHttp.GetUnits), "annotation-lists"),
        Api(nameof(AnnotationsHttp.GetUnitsOfExecution), "annotation-lists"),
        Api(nameof(ConfigurationHttp.GetConfigurations), "configurations"),
        Api(nameof(ConfigurationHttp.GetConfiguration), "configuration"),
        Api(nameof(ConfigurationHttp.SetConfiguration), "configuration", body: "{}"),
        Api(nameof(ConfigurationHttp.PatchConfiguration), "configuration", body: JsonPatch),
        Api(nameof(ConfigurationHttp.DeleteConfiguration), "configuration"),
        Api(nameof(ConfigurationHttp.GetConfigurationResolved), "configuration"),
        Api(nameof(ConfigurationHttp.GetConfigurationComposition), "configuration"),
        Api(nameof(ConfigurationHttp.GetConfigurationViewDiff), "configuration"),
        Api(nameof(ConfigurationHttp.GetConfigurationVersions), "configuration-versions"),
        Api(nameof(ConfigurationHttp.GetConfigurationVersion), "configuration-versions"),
        Api(nameof(ConfigurationHttp.GetConfigurationVersionDiff), "configuration-versions"),
        Api(nameof(ConfigurationHttp.GetConfigurationVersionDiffCustom), "configuration-versions"),
        Api(nameof(ConfigurationHttp.RestoreConfigurationVersion), "configuration-versions"),
        Api(nameof(ConfigurationSchemaHttp.GetConfigurationSchemas), "configuration-schemas"),
        Api(nameof(ConfigurationSchemaHttp.GetConfigurationSchema), "type-schema"),
        Api(nameof(ConfigurationSchemaHttp.SetConfigurationSchema), "type-schema", body: """{"type":"object"}"""),
        Api(nameof(ConfigurationSchemaHttp.DeleteConfigurationSchema), "type-schema"),
        Api(nameof(ConfigurationSchemaHttp.GetSchemaVersions), "type-schema"),
        Api(nameof(ConfigurationSchemaHttp.GetSchemaVersion), "type-schema"),
        Api(nameof(ConfigurationSchemaHttp.GetSchemaVersionDiff), "type-schema"),
        Api(nameof(ConfigurationSchemaHttp.GetSchemaVersionDiffCustom), "type-schema"),
        Api(nameof(ConfigurationSchemaHttp.RestoreSchemaVersion), "type-schema"),
        Api(nameof(ConfigurationSchemaHttp.GetAnnotationSchema), "annotation-schema"),
        Api(nameof(ConfigurationSchemaHttp.SetAnnotationSchema), "annotation-schema", body: """{"type":"object"}"""),
        Api(nameof(ConfigurationSchemaHttp.DeleteAnnotationSchema), "annotation-schema"),
        Api(nameof(ConfigurationSchemaHttp.GetCombinedConfigurationSchema), "annotation-schema"),
        Api(nameof(ConfigurationSchemaHttp.GetAnnotationSchemaVersions), "annotation-schema"),
        Api(nameof(ConfigurationSchemaHttp.GetAnnotationSchemaVersion), "annotation-schema"),
        Api(nameof(ConfigurationSchemaHttp.GetAnnotationSchemaVersionDiff), "annotation-schema"),
        Api(nameof(ConfigurationSchemaHttp.GetAnnotationSchemaVersionDiffCustom), "annotation-schema"),
        Api(nameof(ConfigurationSchemaHttp.RestoreAnnotationSchemaVersion), "annotation-schema"),
        Api(nameof(ConfigurationSchemaHttp.GetDescendantTypeSchema), "descendant-type-schema"),
        Api(nameof(ConfigurationSchemaHttp.SetDescendantTypeSchema), "descendant-type-schema", body: """{"type":"object"}"""),
        Api(nameof(ConfigurationSchemaHttp.DeleteDescendantTypeSchema), "descendant-type-schema"),
        Api(nameof(ConfigurationSchemaHttp.GetDescendantTypeSchemaVersions), "descendant-type-schema"),
        Api(nameof(ConfigurationSchemaHttp.GetDescendantTypeSchemaVersion), "descendant-type-schema"),
        Api(nameof(ConfigurationSchemaHttp.GetDescendantTypeSchemaVersionDiff), "descendant-type-schema"),
        Api(nameof(ConfigurationSchemaHttp.GetDescendantTypeSchemaVersionDiffCustom), "descendant-type-schema"),
        Api(nameof(ConfigurationSchemaHttp.RestoreDescendantTypeSchemaVersion), "descendant-type-schema"),
        Api(nameof(TemplateHttp.GetTemplates), "templates"),
        Api(nameof(TemplateHttp.GetTemplate), "templates"),
        Api(nameof(TemplateHttp.SetTemplate), "templates", body: "{}"),
        Api(nameof(TemplateHttp.PatchTemplate), "templates", body: JsonPatch),
        Api(nameof(TemplateHttp.DeleteTemplate), "templates"),
        Api(nameof(TemplateHttp.GetTemplateVersions), "templates"),
        Api(nameof(TemplateHttp.GetTemplateVersion), "templates"),
        Api(nameof(TemplateHttp.GetTemplateVersionDiff), "templates"),
        Api(nameof(TemplateHttp.GetTemplateVersionDiffCustom), "templates"),
        Api(nameof(TemplateHttp.RestoreTemplateVersion), "templates"),
        Api(nameof(KeyValuesHttp.GetKeyValues), "key-values"),
        Api(nameof(KeyValuesHttp.GetKeyValue), "key-values"),
        Api(nameof(KeyValuesHttp.SetKeyValue), "key-values", body: """{"value":"v"}"""),
        Api(nameof(KeyValuesHttp.DeleteKeyValue), "key-values"),
        Api(nameof(KeyValuesHttp.GetKeyValueVersions), "key-values"),
        Api(nameof(KeyValuesHttp.GetKeyValueVersion), "key-values"),
        Api(nameof(KeyValuesHttp.GetKeyValueVersionDiff), "key-values"),
        Api(nameof(KeyValuesHttp.GetKeyValueVersionDiffCustom), "key-values"),
        Api(nameof(KeyValuesHttp.RestoreKeyValues), "key-value-bulk", body: """{"keys":["sample"]}"""),
        Api(nameof(KeyValuesHttp.PurgeKeyValues), "key-value-bulk", usersOnly: true, body: """{"keys":["sample"]}"""),
        Api(nameof(KeyValuesHttp.ProtectKeyValues), "key-value-bulk", body: """{"keys":["sample"],"protection":"Public"}"""),
        Api(nameof(KeyValuesHttp.ExportKeyValues), "key-value-bulk", body: "{}"),
        Api(nameof(KeyValuesHttp.ImportKeyValues), "key-value-bulk", body: """{"format":"2s.key-values.v1","pairs":[]}"""),
        Api(nameof(ViewTransferHttp.StartViewExport), "view-transfers", body: "{}"),
        Api(nameof(ViewTransferHttp.GetViewExport), "view-transfers"),
        Api(nameof(ViewTransferHttp.GetViewExportArchive), "view-transfers"),
        Api(nameof(ViewTransferHttp.StartViewImport), "view-transfers", body: "{}"),
        Api(nameof(ViewTransferHttp.GetViewImport), "view-transfers"),

        // An access point in the route: v1/access/points/{key}/...
        Key(nameof(AccessHttp.GetAccessPoint), "access-point", TenantGate.Api),
        Key(nameof(AccessHttp.PutAccessPoint), "access-point", TenantGate.Api, body: "{}"),
        Key(nameof(AccessAuditHttp.GetAccessAudit), "access-audit", TenantGate.Api),
        Key(nameof(AccessHttp.GetMachineAuthentication), "machine-authentication", TenantGate.Api),
        Key(nameof(AccessHttp.PutMachineAuthentication), "machine-authentication", TenantGate.Api, body: """{"enabledCredentialKinds":["ApiKey"]}"""),
        Key(nameof(MembersHttp.GetMembers), "members", TenantGate.Service),
        Key(nameof(MembersHttp.SetMemberRole), "members", TenantGate.Service, body: """{"role":"Reader"}"""),
        Key(nameof(MembersHttp.RemoveMember), "members", TenantGate.Service),
        Key(nameof(InvitationsHttp.GetInvitations), "point-invitations", TenantGate.Service),
        Key(nameof(InvitationsHttp.CreateInvitation), "point-invitations", TenantGate.Service, body: """{"email":"eve@globex.test","role":"Reader"}"""),
        Key(nameof(InvitationsHttp.ResendInvitation), "point-invitations", TenantGate.Service),
        Key(nameof(InvitationsHttp.RevokeInvitation), "point-invitations", TenantGate.Service),

        // The tenant in the body.
        Body(nameof(AccessHttp.PostGrantPermission), "grant-revoke", """{"accessPointKey":"{key}","accountId":"{userB}","role":"Reader","createdById":"{userB}"}"""),
        Body(nameof(AccessHttp.PostRevokePermission), "grant-revoke", """{"accessPointKey":"{key}","accountId":"{userB}","role":"Reader","createdById":"{userB}"}"""),
        Body(nameof(AccessHttp.PostAccessPoints), "access-point-create", """{"organization":"{organization}","projects":["{project}"]}"""),
        Body(nameof(AccessHttp.PostAccount), "access-point-create", """{"organization":"{organization}","project":"{project}"}"""),

        // No tenant in the request.
        Other(nameof(AccessHttp.GetAccount), TenantGate.Caller),
        Other(nameof(AccessHttp.GetAccessPoints), TenantGate.Caller),
        Other(nameof(AccessHttp.GetProjects), TenantGate.Caller),
        Other(nameof(InvitationsHttp.GetMyInvitations), TenantGate.Caller),
        Other(nameof(InvitationsHttp.AcceptInvitation), TenantGate.Caller),
        Other(nameof(InvitationsHttp.DeclineInvitation), TenantGate.Caller),
        Other(nameof(ExhibitionHttp.GetExhibition), TenantGate.Caller),
        Other(nameof(ExhibitionHttp.JoinExhibition), TenantGate.Caller),
        Other(nameof(AccessHttp.GetCertificateAuthority), TenantGate.Anonymous),
        Other(nameof(AuthConfigurationHttp.GetAuthConfiguration), TenantGate.Anonymous),
        Other(nameof(TokenExchangeHttp.PostUserToken), TenantGate.Anonymous),
        Other(nameof(AuthorizationServerHttp.GetAuthorizationServerMetadata), TenantGate.Anonymous),
        Other(nameof(AuthorizationServerHttp.GetJsonWebKeySet), TenantGate.Anonymous),
    ];

    // Every [Function] method of the API with an HTTP trigger, by its function name.
    public static IReadOnlyDictionary<string, MethodInfo> Functions { get; } = typeof(AccessHttp).Assembly.GetTypes()
        .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        .Where(method => method.GetCustomAttribute<FunctionAttribute>() is not null && Trigger(method) is not null)
        .ToDictionary(method => method.GetCustomAttribute<FunctionAttribute>()!.Name, StringComparer.Ordinal);

    public static TenantRoute Get(string function) => All.Single(route => route.Function == function);

    public static Microsoft.Azure.Functions.Worker.HttpTriggerAttribute? Trigger(MethodInfo method)
    {
        return method.GetParameters()
            .Select(parameter => parameter.GetCustomAttribute<Microsoft.Azure.Functions.Worker.HttpTriggerAttribute>())
            .SingleOrDefault(trigger => trigger is not null);
    }

    private static TenantRoute Api(string function, string family, bool usersOnly = false, string? body = null)
    {
        return new TenantRoute(function, family, TenantGate.Api, TenantAddress.Route, usersOnly) { Body = body };
    }

    private static TenantRoute Key(string function, string family, TenantGate gate, string? body = null)
    {
        return new TenantRoute(function, family, gate, TenantAddress.Key, UsersOnly: true) { Body = body };
    }

    private static TenantRoute Body(string function, string family, string body)
    {
        return new TenantRoute(function, family, TenantGate.Service, TenantAddress.Body, UsersOnly: true) { Body = body };
    }

    private static TenantRoute Other(string function, TenantGate gate)
    {
        return new TenantRoute(function, gate == TenantGate.Caller ? "caller" : "anonymous", gate, TenantAddress.None);
    }
}
