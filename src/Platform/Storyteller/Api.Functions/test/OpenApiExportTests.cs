using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;

using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Configurations;
using Microsoft.OpenApi;

using ApiOpenApiOptions = _42.Platform.Storyteller.Api.OpenApi.OpenApiConfigurationOptions;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// Renders the OpenAPI document the way the Functions host does (the same engine, options and filters), so the SDKs can be
// regenerated without a running host: set STORYTELLER_OPENAPI_EXPORT to a file path and the rendered document is written there.
public class OpenApiExportTests
{
    public const string ExportVariable = "STORYTELLER_OPENAPI_EXPORT";

    // The API assembly to render; by default the one in the API project's own output directory.
    public const string AssemblyVariable = "STORYTELLER_OPENAPI_ASSEMBLY";

    // Spec 7.4: the routes that refuse machines whatever their credentials carry.
    private static readonly string[] UserOnlyOperations =
    [
        Definitions.RouteIds.Access.GetAccount,
        Definitions.RouteIds.Access.CreateAccount,
        Definitions.RouteIds.Access.GetAccessPoints,
        Definitions.RouteIds.Access.GetAccessPoint,
        Definitions.RouteIds.Access.UpdateAccessPoint,
        Definitions.RouteIds.Access.GetProjects,
        Definitions.RouteIds.Access.CreateAccessPoint,
        Definitions.RouteIds.Access.GrantUserAccess,
        Definitions.RouteIds.Access.RevokeUserAccess,
        Definitions.RouteIds.Access.GetMembers,
        Definitions.RouteIds.Access.SetMemberRole,
        Definitions.RouteIds.Access.RemoveMember,
        Definitions.RouteIds.Access.GetInvitations,
        Definitions.RouteIds.Access.CreateInvitation,
        Definitions.RouteIds.Access.ResendInvitation,
        Definitions.RouteIds.Access.RevokeInvitation,
        Definitions.RouteIds.Access.GetMyInvitations,
        Definitions.RouteIds.Access.AcceptInvitation,
        Definitions.RouteIds.Access.DeclineInvitation,
        Definitions.RouteIds.Access.GetMachineAccesses,
        Definitions.RouteIds.Access.GetMachineAccess,
        Definitions.RouteIds.Access.CreateMachineAccess,
        Definitions.RouteIds.Access.ResetMachineAccess,
        Definitions.RouteIds.Access.DeleteMachineAccess,
        Definitions.RouteIds.Access.GetMachineAuthentication,
        Definitions.RouteIds.Access.SetMachineAuthentication,
        Definitions.RouteIds.Access.GetSharedCertificates,
        Definitions.RouteIds.Access.IssueSharedCertificate,
        Definitions.RouteIds.Access.RevokeSharedCertificate,
        Definitions.RouteIds.Access.GetFederatedIssuers,
        Definitions.RouteIds.Access.GetFederatedIssuer,
        Definitions.RouteIds.Access.SetFederatedIssuer,
        Definitions.RouteIds.Access.DeleteFederatedIssuer,
        Definitions.RouteIds.Access.SetMachineFederationRules,
        Definitions.RouteIds.Annotations.PurgeAnnotation,
        Definitions.RouteIds.Views.CreateView,
        Definitions.RouteIds.Views.UpdateView,
        Definitions.RouteIds.KeyValues.PurgeKeyValues,
        Definitions.RouteIds.KeyValues.SetKeyValuePolicy,
        Definitions.RouteIds.KeyValues.GetByokSetup,
        Definitions.RouteIds.KeyValues.VerifyByok,
        Definitions.RouteIds.KeyValues.RotateKeyValueKey,
        Definitions.RouteIds.KeyValues.AddRecipient,
        Definitions.RouteIds.KeyValues.UpdateRecipient,
        Definitions.RouteIds.KeyValues.RemoveRecipient,
        Definitions.RouteIds.KeyValues.GetKeyValueAudit,
        Definitions.RouteIds.KeyValues.GetKeyValueUsage,
    ];

    // Without a bearer token: the token exchange, the public CA certificate and the sign-in configuration are anonymous,
    // and a machine renews its certificate with the certificate itself.
    private static readonly string[] AnonymousOperations =
    [
        Definitions.RouteIds.Access.ExchangeMachineToken,
        Definitions.RouteIds.Access.GetCertificateAuthority,
        Definitions.RouteIds.Access.GetAuthConfiguration,
        Definitions.RouteIds.Access.RenewMachineCertificate,
    ];

    [Fact]
    public async Task Document_HasEveryKeyValueOperation_UnderTheKeyValuesTag()
    {
        var json = await RenderAsync();
        using var document = JsonDocument.Parse(json);

        var operations = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Select(operation => operation.Value)
            .ToList();
        var keyValueOperations = operations
            .Where(operation => operation.GetProperty("tags").EnumerateArray().Any(tag => tag.GetString() == Definitions.Tags.KeyValues))
            .Select(operation => operation.GetProperty("operationId").GetString())
            .ToList();
        var expected = typeof(Definitions.RouteIds.KeyValues)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (string)field.GetValue(null)!)
            .ToList();

        keyValueOperations.ShouldBe(expected, ignoreOrder: true);
        document.RootElement.GetProperty("components").GetProperty("schemas").TryGetProperty("KeyValuePolicy", out _).ShouldBeTrue();
        document.RootElement.GetProperty("info").GetProperty("version").GetString().ShouldMatch(@"^\d+\.\d+\.\d+\.\d+$", "the file version of the API assembly, which names the exported document");
        document.RootElement.GetProperty("servers")[0].GetProperty("url").GetString().ShouldBe("http://localhost:7071/api");

        var exportPath = Environment.GetEnvironmentVariable(ExportVariable);

        if (!string.IsNullOrWhiteSpace(exportPath))
        {
            await File.WriteAllTextAsync(exportPath, json);
        }
    }

    [Fact]
    public async Task Document_DiffOperations_OfferUnifiedText()
    {
        var json = await RenderAsync();
        using var document = JsonDocument.Parse(json);

        var diff = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Select(operation => operation.Value)
            .Single(operation => operation.GetProperty("operationId").GetString() == Definitions.RouteIds.KeyValues.GetKeyValueVersionDiff);

        diff.GetProperty("responses").GetProperty("200").GetProperty("content").TryGetProperty("text/plain", out _).ShouldBeTrue();
    }

    // The export and import of a view: five operations below the view, the include filter as a comma-separated list of
    // kinds, and no route of the first spec below configurations/.
    [Fact]
    public async Task Document_HasTheViewTransferOperations_UnderTheTransfersTag()
    {
        var json = await RenderAsync();
        using var document = JsonDocument.Parse(json);

        var paths = document.RootElement.GetProperty("paths").EnumerateObject().ToList();
        var operations = paths
            .SelectMany(path => path.Value.EnumerateObject().Select(operation => (Path: path.Name, Method: operation.Name, Operation: operation.Value)))
            .Where(operation => operation.Operation.GetProperty("tags").EnumerateArray().Any(tag => tag.GetString() == Definitions.Tags.Transfers))
            .ToList();

        operations.Select(operation => $"{operation.Method.ToUpperInvariant()} {operation.Path} {operation.Operation.GetProperty("operationId").GetString()}").ShouldBe(
            [
                "POST /v1/{organization}/{project}/{view}/exports StartViewExport",
                "GET /v1/{organization}/{project}/{view}/exports/{id} GetViewExport",
                "GET /v1/{organization}/{project}/{view}/exports/{id}/archive GetViewExportArchive",
                "POST /v1/{organization}/{project}/{view}/imports StartViewImport",
                "GET /v1/{organization}/{project}/{view}/imports/{id} GetViewImport",
            ],
            ignoreOrder: true);
        paths.Select(path => path.Name).ShouldNotContain(path => path.Contains("/configurations/exports", StringComparison.Ordinal) || path.Contains("/configurations/imports", StringComparison.Ordinal));
        operations.Where(operation => !operation.Operation.GetProperty("responses").TryGetProperty("403", out _)).ShouldBeEmpty("every operation documents its 403");

        var import = operations.Single(operation => operation.Path.EndsWith("/imports", StringComparison.Ordinal)).Operation;
        var parameters = import.GetProperty("parameters").EnumerateArray()
            .Where(parameter => parameter.GetProperty("in").GetString() == "query")
            .ToDictionary(parameter => parameter.GetProperty("name").GetString()!);
        parameters.Keys.ShouldBe(["include", "existing", "force", "dryRun", "fileName"], ignoreOrder: true);

        var include = parameters["include"];
        include.GetProperty("style").GetString().ShouldBe("form");
        include.GetProperty("explode").GetBoolean().ShouldBeFalse("comma-separated");
        include.GetProperty("schema").GetProperty("type").GetString().ShouldBe("array");
        include.GetProperty("schema").GetProperty("items").GetProperty("$ref").GetString().ShouldBe("#/components/schemas/ViewArchiveContent");
        parameters["existing"].GetProperty("schema").GetProperty("$ref").GetString().ShouldBe("#/components/schemas/ViewImportExisting");

        // the kinds and the existing option are shared schemas, so both SDKs have one type for each
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var content = schemas.GetProperty("ViewArchiveContent");
        content.GetProperty("enum").EnumerateArray().Select(kind => kind.GetString()).ShouldBe(["Annotations", "Schemas", "Templates", "Configurations"]);
        content.TryGetProperty("default", out _).ShouldBeFalse("no include means all four kinds, not the first");
        var existing = schemas.GetProperty("ViewImportExisting");
        existing.GetProperty("enum").EnumerateArray().Select(value => value.GetString()).ShouldBe(["Overwrite", "Skip"]);
        existing.GetProperty("default").GetString().ShouldBe("Overwrite");

        foreach (var name in new[] { "ViewExportCreate", "ViewExport", "ViewImport" })
        {
            schemas.GetProperty(name).GetProperty("properties").GetProperty("Include").GetProperty("items").GetProperty("$ref").GetString()
                .ShouldBe("#/components/schemas/ViewArchiveContent", name);
        }

        schemas.GetProperty("ViewImport").GetProperty("properties").GetProperty("Existing").GetProperty("$ref").GetString()
            .ShouldBe("#/components/schemas/ViewImportExisting");
    }

    // The token endpoint speaks OAuth: a form body and snake_case JSON, named the same in the document and on the wire.
    [Fact]
    public async Task Document_TokenExchange_UsesTheOAuthFormAndSnakeCaseModels()
    {
        var json = await RenderAsync();
        using var document = JsonDocument.Parse(json);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        PropertyNames(schemas, "MachineTokenRequest").ShouldBe(
            ["grant_type", "subject_token", "subject_token_type", "client_id", "audience", "resource", "scope", "requested_token_type"],
            ignoreOrder: true);
        RequiredNames(schemas, "MachineTokenRequest").ShouldBe(["grant_type", "subject_token", "subject_token_type", "client_id"], ignoreOrder: true);
        PropertyNames(schemas, "MachineTokenResponse").ShouldBe(["access_token", "issued_token_type", "token_type", "expires_in", "scope"], ignoreOrder: true);
        PropertyNames(schemas, "OAuthErrorResponse").ShouldBe(["error", "error_description"], ignoreOrder: true);

        var exchange = Operation(document, Definitions.RouteIds.Access.ExchangeMachineToken);
        exchange.GetProperty("requestBody").GetProperty("content").EnumerateObject().Select(content => content.Name)
            .ShouldBe([Definitions.ContentTypes.FormUrlEncoded]);
        exchange.GetProperty("responses").GetProperty("400").GetProperty("content").GetProperty(Definitions.ContentTypes.Json)
            .GetProperty("schema").GetProperty("$ref").GetString().ShouldBe("#/components/schemas/OAuthErrorResponse");
        exchange.GetProperty("responses").TryGetProperty("503", out _).ShouldBeTrue();

        // Anonymous: no security requirement and no 403; the management operations have both.
        (exchange.TryGetProperty("security", out var security) && security.GetArrayLength() > 0).ShouldBeFalse();
        exchange.GetProperty("responses").TryGetProperty("403", out _).ShouldBeFalse();

        foreach (var routeId in new[]
                 {
                     Definitions.RouteIds.Access.GetFederatedIssuers,
                     Definitions.RouteIds.Access.GetFederatedIssuer,
                     Definitions.RouteIds.Access.SetFederatedIssuer,
                     Definitions.RouteIds.Access.DeleteFederatedIssuer,
                     Definitions.RouteIds.Access.SetMachineFederationRules,
                 })
        {
            var operation = Operation(document, routeId);
            operation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()).ShouldBe([Definitions.Tags.Access]);
            operation.GetProperty("security").GetArrayLength().ShouldBeGreaterThan(0);
            operation.GetProperty("responses").TryGetProperty("403", out _).ShouldBeTrue();
        }

        // Both PUTs can answer 409 (a re-created issuer name in use, a machine changed concurrently), so the SDKs model it.
        foreach (var routeId in new[] { Definitions.RouteIds.Access.SetFederatedIssuer, Definitions.RouteIds.Access.SetMachineFederationRules })
        {
            Operation(document, routeId).GetProperty("responses").GetProperty("409").GetProperty("content").GetProperty(Definitions.ContentTypes.Json)
                .GetProperty("schema").GetProperty("$ref").GetString().ShouldBe("#/components/schemas/ErrorResponse");
        }

        PropertyNames(schemas, "FederatedIssuer").ShouldContain("RequiredClaims");
        PropertyNames(schemas, "MachineAccess").ShouldContain("FederationRules");
    }

    // The scopes of a machine (424-79): one list on the create model and the machine, of one shared enum of the 11
    // scopes without a default, so both SDKs have one type for it and send nothing on their own. The presets and the
    // separate management list are gone, also from every description.
    [Fact]
    public async Task Document_Scopes_AreAListOfTheSharedMachineScopeEnum()
    {
        var json = await RenderAsync();
        using var document = JsonDocument.Parse(json);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        foreach (var name in new[] { "MachineAccess", "MachineAccessCreate" })
        {
            PropertyNames(schemas, name).ShouldNotContain("Scope", name);
            PropertyNames(schemas, name).ShouldNotContain("ManagementScopes", name);

            var property = schemas.GetProperty(name).GetProperty("properties").GetProperty("Scopes");
            property.GetProperty("type").GetString().ShouldBe("array", name);
            property.GetProperty("items").GetProperty("$ref").GetString().ShouldBe("#/components/schemas/MachineScope", name);
            property.TryGetProperty("default", out _).ShouldBeFalse(name);
        }

        // Not required on the create model: a body without the list reaches the handler, whose 400 names the runtime scopes.
        (schemas.GetProperty("MachineAccessCreate").TryGetProperty("required", out var required) ? required.EnumerateArray().Select(item => item.GetString()) : [])
            .ShouldNotContain("Scopes");

        var scope = schemas.GetProperty("MachineScope");
        scope.GetProperty("type").GetString().ShouldBe("string");
        scope.GetProperty("enum").EnumerateArray().Select(value => value.GetString()).ShouldBe(
            [
                "AnnotationRead",
                "AnnotationWrite",
                "ConfigurationRead",
                "ConfigurationWrite",
                "SecretsRead",
                "SecretsWrite",
                "ConfigurationTransfer",
                "SchemaWrite",
                "TemplateWrite",
                "AnnotationManage",
                "KeyValueManage",
            ]);
        scope.TryGetProperty("default", out _).ShouldBeFalse("a scope is only ever granted on purpose");

        // One scope type: no preset enum, no management enum and no enum per list.
        schemas.EnumerateObject().Select(schema => schema.Name).Where(name => name.Contains("Scope", StringComparison.Ordinal)).ShouldBe(["MachineScope"]);

        foreach (var stale in new[] { "DefaultRead", "SecretsReadWrite", "AnnotationReadWrite", "ConfigurationReadWrite", "Default.Read", "Annotation.ReadWrite", "Configuration.ReadWrite", "Configuration.Secrets" })
        {
            json.ShouldNotContain(stale, Case.Sensitive);
        }
    }

    // Spec 7.5 and 8.2: every operation a machine may call documents a 403 that names exactly the machine scopes it
    // needs (the key/value ones also the secret rights and the mTLS rule), and no 401 names a scope: a machine without
    // one is authenticated and gets 403. The routes for users only document the 401 of User.Impersonation and a 403 for
    // machines. Every operation of the document is in one of the tables, so a new one cannot forget its scopes.
    [Fact]
    public async Task Document_EveryOperationForMachines_NamesItsScopesInIts403()
    {
        var json = await RenderAsync();
        using var document = JsonDocument.Parse(json);
        var operations = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Select(operation => (Id: operation.Value.GetProperty("operationId").GetString()!, Responses: operation.Value.GetProperty("responses")))
            .ToList();
        var machineOperations = MachineOperations();

        operations.Select(operation => operation.Id).Distinct()
            .ShouldBe([.. machineOperations.Keys, .. UserOnlyOperations, .. AnonymousOperations], ignoreOrder: true);

        foreach (var (id, responses) in operations)
        {
            var unauthorized = responses.TryGetProperty("401", out var response401) ? response401.GetProperty("description").GetString()! : string.Empty;
            NamedScopes(unauthorized).ShouldBeEmpty(id);

            if (machineOperations.TryGetValue(id, out var expected))
            {
                var forbidden = responses.GetProperty("403");
                var description = forbidden.GetProperty("description").GetString()!;
                description.ShouldBe(expected.Forbidden, id);
                NamedScopes(description).ShouldBe(expected.Scopes, ignoreOrder: true, customMessage: id);
                forbidden.GetProperty("content").GetProperty(Definitions.ContentTypes.Json).GetProperty("schema").GetProperty("$ref").GetString()
                    .ShouldBe("#/components/schemas/ErrorResponse", id);
            }
            else if (UserOnlyOperations.Contains(id))
            {
                unauthorized.ShouldBe(Definitions.Descriptions.ResponseUnauthorizedUserOnly, id);
                var description = responses.GetProperty("403").GetProperty("description").GetString()!;
                description.ShouldStartWith(Definitions.Descriptions.ResponseForbiddenUserOnly, customMessage: id, caseSensitivity: Case.Sensitive);
                NamedScopes(description).ShouldBeEmpty(id);
            }
            else
            {
                responses.TryGetProperty("403", out _).ShouldBeFalse(id);
            }
        }

        // The texts of spec 8.2, as the clients read them.
        Definitions.Descriptions.ResponseForbiddenAnnotationRead.ShouldBe(Definitions.Descriptions.ResponseForbidden + " A machine needs the scope Annotation.Read (AccessDenied).");
        Definitions.Descriptions.ResponseForbiddenManagementSchemaWrite.ShouldBe(Definitions.Descriptions.ResponseForbidden + " A machine needs the scopes Configuration.Write and Schema.Write (AccessDenied).");
        Definitions.Descriptions.ResponseUnauthorized.ShouldBe("Authentication issues: missing or invalid credentials, a user token without User.Impersonation, or a machine the project does not know or whose credential kind it does not enable.");
        Definitions.Descriptions.ResponseUnauthorizedUserOnly.ShouldContain(Scopes.User.Impersonation);
        Definitions.Descriptions.ResponseForbiddenUserOnly.ShouldContain("a machine is refused (ErrorCode AccessDenied)");
    }

    // Spec 3 and 4: the 400 of a new machine names an undefined scope and a list without a runtime scope, and its 403
    // and those of the reset, the federation rules and the delete name the role the scopes of the machine need.
    [Fact]
    public async Task Document_MachineRoutes_NameTheScopeRulesInTheir400AndTheRoleRuleInTheir403()
    {
        var json = await RenderAsync();
        using var document = JsonDocument.Parse(json);

        Operation(document, Definitions.RouteIds.Access.CreateMachineAccess).GetProperty("responses").GetProperty("400").GetProperty("description").GetString()
            .ShouldBe("The annotation key is not valid (ErrorCode InvalidKey), a scope doesn't exist, the scopes have no runtime scope, or the credential kind is not supported or its identity provider cannot carry one of the scopes.");

        foreach (var routeId in new[] { Definitions.RouteIds.Access.CreateMachineAccess, Definitions.RouteIds.Access.ResetMachineAccess, Definitions.RouteIds.Access.SetMachineFederationRules, Definitions.RouteIds.Access.DeleteMachineAccess })
        {
            Operation(document, routeId).GetProperty("responses").GetProperty("403").GetProperty("description").GetString()
                .ShouldBe(Definitions.Descriptions.ResponseForbiddenMachineScopes, routeId);
        }

        Definitions.Descriptions.ResponseForbiddenMachineScopes.ShouldBe(
            Definitions.Descriptions.ResponseForbiddenUserOnly
            + " Creating a machine, resetting it, replacing its federation rules or deleting it needs the Contributor role, ContributorWithSecrets when the machine has a Secrets scope and Administrator when it has a management scope (ErrorCode AccessDenied).");
    }

    // The description of a project (424-78): its update and the caller's projects, under the Access tag, for users, with
    // the 403 of the document filter. The list carries the key, the role and the description, not the members.
    [Fact]
    public async Task Document_ProjectOperations_ArePublished()
    {
        var json = await RenderAsync();
        using var document = JsonDocument.Parse(json);

        var operations = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject().Select(operation => (Path: path.Name, Method: operation.Name, Operation: operation.Value)))
            .Where(operation => operation.Operation.GetProperty("operationId").GetString() is Definitions.RouteIds.Access.UpdateAccessPoint or Definitions.RouteIds.Access.GetProjects)
            .ToList();

        operations.Select(operation => $"{operation.Method.ToUpperInvariant()} {operation.Path} {operation.Operation.GetProperty("operationId").GetString()}").ShouldBe(
            [
                "PUT /v1/access/points/{key} UpdateAccessPoint",
                "GET /v1/access/projects GetProjects",
            ],
            ignoreOrder: true);

        foreach (var (_, _, operation) in operations)
        {
            operation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()).ShouldBe([Definitions.Tags.Access]);
            operation.GetProperty("security").GetArrayLength().ShouldBeGreaterThan(0);
            operation.GetProperty("responses").GetProperty("403").GetProperty("description").GetString().ShouldBe(Definitions.Descriptions.ResponseForbiddenUserOnly);
        }

        var update = Operation(document, Definitions.RouteIds.Access.UpdateAccessPoint);
        update.GetProperty("responses").EnumerateObject().Select(response => response.Name).ShouldBe(["200", "400", "401", "403", "404", "409", "500"], ignoreOrder: true);
        update.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty(Definitions.ContentTypes.Json)
            .GetProperty("schema").GetProperty("$ref").GetString().ShouldBe("#/components/schemas/AccessPoint");

        var projects = Operation(document, Definitions.RouteIds.Access.GetProjects);
        projects.GetProperty("responses").EnumerateObject().Select(response => response.Name).ShouldBe(["200", "401", "403", "404", "500"], ignoreOrder: true);
        var list = projects.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty(Definitions.ContentTypes.Json).GetProperty("schema");
        list.GetProperty("type").GetString().ShouldBe("array");
        list.GetProperty("items").GetProperty("$ref").GetString().ShouldBe("#/components/schemas/ProjectSummary");

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        PropertyNames(schemas, "ProjectSummary").ShouldBe(["Key", "Role", "Description"], ignoreOrder: true);
        PropertyNames(schemas, "AccessPointUpdate").ShouldBe(["Description"]);
        PropertyNames(schemas, "AccessPoint").ShouldContain("Description");
        PropertyNames(schemas, "AccessPointCreate").ShouldContain("ProjectDescription");
        PropertyNames(schemas, "AccountCreate").ShouldContain("ProjectDescription");
    }

    // These handlers read their JSON bodies themselves, not through [FromBody], so [OpenApiRequestBody] alone documents
    // them: each keeps its application/json body, its schema and its required flag, and the SDKs do not change. The
    // generator writes "required" only when it is true; ExportKeyValues sets Required = false and the others nothing.
    [Fact]
    public async Task Document_JsonRequestBodies_KeepTheirSchemaAndRequiredFlag()
    {
        var json = await RenderAsync();
        using var document = JsonDocument.Parse(json);

        var bodies = new (string OperationId, string Schema, bool Required)[]
        {
            (Definitions.RouteIds.Access.CreateAccount, Reference("AccountCreate"), false),
            (Definitions.RouteIds.Access.CreateAccessPoint, Reference("AccessPointCreate"), false),
            (Definitions.RouteIds.Access.UpdateAccessPoint, Reference("AccessPointUpdate"), false),
            (Definitions.RouteIds.Access.GrantUserAccess, Reference("Permission"), false),
            (Definitions.RouteIds.Access.RevokeUserAccess, Reference("Permission"), false),
            (Definitions.RouteIds.Access.CreateMachineAccess, Reference("MachineAccessCreate"), false),
            (Definitions.RouteIds.Access.SetMachineAuthentication, Reference("MachineAuthenticationPolicy"), false),
            (Definitions.RouteIds.Access.IssueSharedCertificate, Reference("SharedCertificateCreate"), false),
            (Definitions.RouteIds.Access.CreateInvitation, Reference("InvitationCreate"), false),
            (Definitions.RouteIds.Access.SetMemberRole, Reference("MemberRoleUpdate"), false),
            (Definitions.RouteIds.Views.CreateView, Reference("ViewCreate"), false),
            (Definitions.RouteIds.Views.UpdateView, Reference("ViewUpdate"), false),
            (Definitions.RouteIds.KeyValues.SetKeyValue, Reference("KeyValueWrite"), true),
            (Definitions.RouteIds.KeyValues.RestoreKeyValues, Reference("KeyValueBulkRequest"), true),
            (Definitions.RouteIds.KeyValues.PurgeKeyValues, Reference("KeyValueBulkRequest"), true),
            (Definitions.RouteIds.KeyValues.ProtectKeyValues, Reference("KeyValueBulkRequest"), true),
            (Definitions.RouteIds.KeyValues.ExportKeyValues, Reference("KeyValueExportRequest"), false),
            (Definitions.RouteIds.KeyValues.ImportKeyValues, Reference("KeyValueDocument"), true),
            (Definitions.RouteIds.KeyValues.SetKeyValuePolicy, Reference("KeyValuePolicyUpdate"), true),
            (Definitions.RouteIds.KeyValues.RotateKeyValueKey, Reference("KeyValueRotateRequest"), true),
            (Definitions.RouteIds.KeyValues.AddRecipient, Reference("ClientEncryptionRecipientCreate"), true),
            (Definitions.RouteIds.KeyValues.UpdateRecipient, Reference("ClientEncryptionRecipientUpdate"), true),
            (Definitions.RouteIds.Annotations.SetAnnotations, $$"""{"type":"array","items":{{Reference("Annotation")}}}""", false),
            (Definitions.RouteIds.Annotations.SetAnnotationsSimple, """{"type":"array","items":{"type":"string"}}""", false),
            (Definitions.RouteIds.Annotations.FindDeletedAnnotations, Reference("AnnotationKeysRequest"), false),
            (Definitions.RouteIds.Annotations.RestoreAnnotations, Reference("AnnotationRestoreRequest"), false),
        };

        foreach (var (operationId, schema, required) in bodies)
        {
            var body = Operation(document, operationId).GetProperty("requestBody");
            var content = body.GetProperty("content");

            content.EnumerateObject().Select(type => type.Name).ShouldBe([Definitions.ContentTypes.Json], operationId);
            JsonSerializer.Serialize(content.GetProperty(Definitions.ContentTypes.Json).GetProperty("schema")).ShouldBe(schema, operationId);
            (body.TryGetProperty("required", out var flag) ? flag.GetRawText() : null).ShouldBe(required ? "true" : null, operationId);
        }

        static string Reference(string model) => $$"""{"$ref":"#/components/schemas/{{model}}"}""";
    }

    internal static async Task<string> RenderAsync()
    {
        // the same context the extension builds for the host, with the API's own options and filters
        var assembly = LoadApiAssembly();
        var options = new ApiOpenApiOptions();
        var context = new OpenApiHttpTriggerContext(options, new DefaultOpenApiHttpTriggerAuthorization(), new DefaultOpenApiCustomUIOptions(assembly));
        var document = context.Document
            .InitialiseDocument()
            .AddMetadata(options.Info)
            .AddServer(new LocalHostRequest(), "api", options)
            .AddNamingStrategy(options.NamingStrategy)
            .AddVisitors(context.GetVisitorCollection())
            .Build(assembly, options.OpenApiVersion)
            .ApplyDocumentFilters(context.GetDocumentFilterCollection());

        return await document.RenderAsync(OpenApiSpecVersion.OpenApi3_0, OpenApiFormat.Json);
    }

    private static List<string> PropertyNames(JsonElement schemas, string schema)
    {
        return schemas.GetProperty(schema).GetProperty("properties").EnumerateObject().Select(property => property.Name).ToList();
    }

    private static List<string?> RequiredNames(JsonElement schemas, string schema)
    {
        return schemas.GetProperty(schema).GetProperty("required").EnumerateArray().Select(name => name.GetString()).ToList();
    }

    private static JsonElement Operation(JsonDocument document, string operationId)
    {
        return Operations(document, operationId).Single();
    }

    // One function can answer several methods (SetTemplate: POST and PUT), each with the same operation id.
    private static List<JsonElement> Operations(JsonDocument document, string operationId)
    {
        return document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Select(operation => operation.Value)
            .Where(operation => operation.GetProperty("operationId").GetString() == operationId)
            .ToList();
    }

    // The machine scope strings a description names; none of them is a part of another.
    private static List<string> NamedScopes(string description)
    {
        return MachineScopes.ToScopeStrings(MachineScopes.All).Where(scope => description.Contains(scope, StringComparison.Ordinal)).ToList();
    }

    // The operations machines call, by the table of spec 7.5: the 403 each documents and the machine scopes it names,
    // with the secrets scopes of the key/value routes and the per-kind scopes of the view transfer.
    private static Dictionary<string, (string Forbidden, string[] Scopes)> MachineOperations()
    {
        var annotationRead = (Definitions.Descriptions.ResponseForbiddenAnnotationRead, new[] { Scopes.Annotation.Read });
        var annotationManage = (Definitions.Descriptions.ResponseForbiddenManagementAnnotationManage, new[] { Scopes.Annotation.Write, Scopes.Annotation.Manage });
        var configurationRead = (Definitions.Descriptions.ResponseForbiddenConfigurationRead, new[] { Scopes.Configuration.Read });
        var configurationWrite = (Definitions.Descriptions.ResponseForbiddenConfigurationWrite, new[] { Scopes.Configuration.Write });
        var schemaWrite = (Definitions.Descriptions.ResponseForbiddenManagementSchemaWrite, new[] { Scopes.Configuration.Write, Scopes.Schema.Write });
        var templateWrite = (Definitions.Descriptions.ResponseForbiddenManagementTemplateWrite, new[] { Scopes.Configuration.Write, Scopes.Template.Write });
        var secretRead = (Definitions.Descriptions.ResponseMtlsRequiredConfigurationRead, new[] { Scopes.Configuration.Read, Scopes.Secrets.Read });
        var export = (Definitions.Descriptions.ResponseTransferForbiddenExport, new[] { Scopes.Configuration.Transfer, Scopes.Annotation.Read, Scopes.Configuration.Read });
        var import = (Definitions.Descriptions.ResponseTransferForbiddenImport, new[] { Scopes.Configuration.Transfer, Scopes.Annotation.Write, Scopes.Annotation.Manage, Scopes.Configuration.Write, Scopes.Schema.Write, Scopes.Template.Write });

        return new Dictionary<string, (string Forbidden, string[] Scopes)>
        {
            [Definitions.RouteIds.Annotations.GetAnnotations] = annotationRead,
            [Definitions.RouteIds.Annotations.GetAnnotation] = annotationRead,
            [Definitions.RouteIds.Annotations.GetDescendants] = annotationRead,
            [Definitions.RouteIds.Annotations.GetResponsibilities] = annotationRead,
            [Definitions.RouteIds.Annotations.GetSubjects] = annotationRead,
            [Definitions.RouteIds.Annotations.GetUsages] = annotationRead,
            [Definitions.RouteIds.Annotations.GetContexts] = annotationRead,
            [Definitions.RouteIds.Annotations.GetExecutions] = annotationRead,
            [Definitions.RouteIds.Annotations.GetUnits] = annotationRead,
            [Definitions.RouteIds.Annotations.GetUnitsOfExecution] = annotationRead,
            [Definitions.RouteIds.Annotations.GetDeletedAnnotations] = annotationRead,
            [Definitions.RouteIds.Annotations.GetDeletedAnnotation] = annotationRead,
            [Definitions.RouteIds.Annotations.FindDeletedAnnotations] = annotationRead,
            [Definitions.RouteIds.Annotations.GetAnnotationHistory] = annotationRead,
            [Definitions.RouteIds.Annotations.SetAnnotation] = (Definitions.Descriptions.ResponseForbiddenAnnotationWrite, [Scopes.Annotation.Write]),
            [Definitions.RouteIds.Annotations.SetAnnotations] = annotationManage,
            [Definitions.RouteIds.Annotations.SetAnnotationsSimple] = annotationManage,
            [Definitions.RouteIds.Annotations.DeleteAnnotation] = annotationManage,
            [Definitions.RouteIds.Annotations.RestoreAnnotations] = annotationManage,
            [Definitions.RouteIds.Configuration.GetConfigurations] = configurationRead,
            [Definitions.RouteIds.Configuration.GetConfiguration] = configurationRead,
            [Definitions.RouteIds.Configuration.GetResolvedConfiguration] = configurationRead,
            [Definitions.RouteIds.Configuration.GetConfigurationVersions] = configurationRead,
            [Definitions.RouteIds.Configuration.GetConfigurationVersion] = configurationRead,
            [Definitions.RouteIds.Configuration.GetConfigurationVersionDiff] = configurationRead,
            [Definitions.RouteIds.Configuration.GetConfigurationVersionDiffCustom] = configurationRead,
            [Definitions.RouteIds.Configuration.GetConfigurationViewDiff] = configurationRead,
            [Definitions.RouteIds.Configuration.GetConfigurationComposition] = configurationRead,
            [Definitions.RouteIds.Configuration.SetConfiguration] = configurationWrite,
            [Definitions.RouteIds.Configuration.PatchConfiguration] = configurationWrite,
            [Definitions.RouteIds.Configuration.DeleteConfiguration] = configurationWrite,
            [Definitions.RouteIds.ConfigurationSchema.GetConfigurationSchemas] = configurationRead,
            [Definitions.RouteIds.ConfigurationSchema.GetConfigurationSchema] = configurationRead,
            [Definitions.RouteIds.ConfigurationSchema.GetAnnotationSchema] = configurationRead,
            [Definitions.RouteIds.ConfigurationSchema.GetDescendantTypeSchema] = configurationRead,
            [Definitions.RouteIds.ConfigurationSchema.GetCombinedConfigurationSchema] = configurationRead,
            [Definitions.RouteIds.ConfigurationSchema.GetSchemaVersions] = configurationRead,
            [Definitions.RouteIds.ConfigurationSchema.GetSchemaVersion] = configurationRead,
            [Definitions.RouteIds.ConfigurationSchema.GetSchemaVersionDiff] = configurationRead,
            [Definitions.RouteIds.ConfigurationSchema.GetSchemaVersionDiffCustom] = configurationRead,
            [Definitions.RouteIds.ConfigurationSchema.GetAnnotationSchemaVersions] = configurationRead,
            [Definitions.RouteIds.ConfigurationSchema.GetAnnotationSchemaVersion] = configurationRead,
            [Definitions.RouteIds.ConfigurationSchema.GetAnnotationSchemaVersionDiff] = configurationRead,
            [Definitions.RouteIds.ConfigurationSchema.GetAnnotationSchemaVersionDiffCustom] = configurationRead,
            [Definitions.RouteIds.ConfigurationSchema.GetDescendantTypeSchemaVersions] = configurationRead,
            [Definitions.RouteIds.ConfigurationSchema.GetDescendantTypeSchemaVersion] = configurationRead,
            [Definitions.RouteIds.ConfigurationSchema.GetDescendantTypeSchemaVersionDiff] = configurationRead,
            [Definitions.RouteIds.ConfigurationSchema.GetDescendantTypeSchemaVersionDiffCustom] = configurationRead,
            [Definitions.RouteIds.ConfigurationSchema.SetConfigurationSchema] = schemaWrite,
            [Definitions.RouteIds.ConfigurationSchema.DeleteConfigurationSchema] = schemaWrite,
            [Definitions.RouteIds.ConfigurationSchema.SetAnnotationSchema] = schemaWrite,
            [Definitions.RouteIds.ConfigurationSchema.DeleteAnnotationSchema] = schemaWrite,
            [Definitions.RouteIds.ConfigurationSchema.SetDescendantTypeSchema] = schemaWrite,
            [Definitions.RouteIds.ConfigurationSchema.DeleteDescendantTypeSchema] = schemaWrite,
            [Definitions.RouteIds.Template.GetTemplates] = configurationRead,
            [Definitions.RouteIds.Template.GetTemplate] = configurationRead,
            [Definitions.RouteIds.Template.GetTemplateVersions] = configurationRead,
            [Definitions.RouteIds.Template.GetTemplateVersion] = configurationRead,
            [Definitions.RouteIds.Template.GetTemplateVersionDiff] = configurationRead,
            [Definitions.RouteIds.Template.GetTemplateVersionDiffCustom] = configurationRead,
            [Definitions.RouteIds.Template.SetTemplate] = templateWrite,
            [Definitions.RouteIds.Template.PatchTemplate] = templateWrite,
            [Definitions.RouteIds.Template.DeleteTemplate] = templateWrite,
            [Definitions.RouteIds.Bindings.GetBindingCatalog] = (Definitions.Descriptions.ResponseForbiddenBindingCatalog, [Scopes.Configuration.Read]),
            [Definitions.RouteIds.Views.GetViews] = (Definitions.Descriptions.ResponseForbiddenViews, [Scopes.Annotation.Read, Scopes.Configuration.Read]),
            [Definitions.RouteIds.KeyValues.GetKeyValues] = configurationRead,
            [Definitions.RouteIds.KeyValues.GetKeyValue] = secretRead,
            [Definitions.RouteIds.KeyValues.GetKeyValueVersions] = configurationRead,
            [Definitions.RouteIds.KeyValues.GetKeyValueVersion] = secretRead,
            [Definitions.RouteIds.KeyValues.GetKeyValueVersionDiff] = secretRead,
            [Definitions.RouteIds.KeyValues.GetKeyValueVersionDiffCustom] = secretRead,
            [Definitions.RouteIds.KeyValues.GetKeyValuePolicy] = configurationRead,
            [Definitions.RouteIds.KeyValues.GetRecipients] = configurationRead,
            [Definitions.RouteIds.KeyValues.SetKeyValue] = (Definitions.Descriptions.ResponseMtlsRequiredConfigurationWrite, [Scopes.Configuration.Write, Scopes.Secrets.Write]),
            [Definitions.RouteIds.KeyValues.DeleteKeyValue] = (Definitions.Descriptions.ResponseMtlsRequiredDeleteKeyValue, [Scopes.Configuration.Write, Scopes.Secrets.Write]),
            [Definitions.RouteIds.KeyValues.RestoreKeyValues] = (Definitions.Descriptions.ResponseForbiddenManagementKeyValueManage, [Scopes.Configuration.Write, Scopes.KeyValue.Manage, Scopes.Secrets.Write]),
            [Definitions.RouteIds.KeyValues.ProtectKeyValues] = (Definitions.Descriptions.ResponseMtlsRequiredKeyValueManage, [Scopes.Configuration.Write, Scopes.KeyValue.Manage, Scopes.Secrets.Write]),
            [Definitions.RouteIds.KeyValues.ExportKeyValues] = (Definitions.Descriptions.ResponseMtlsRequiredKeyValueExport, [Scopes.Configuration.Read, Scopes.Configuration.Transfer, Scopes.Secrets.Read]),
            [Definitions.RouteIds.KeyValues.ImportKeyValues] = (Definitions.Descriptions.ResponseMtlsRequiredKeyValueImport, [Scopes.Configuration.Write, Scopes.Configuration.Transfer, Scopes.Secrets.Write]),
            [Definitions.RouteIds.Transfers.StartViewExport] = export,
            [Definitions.RouteIds.Transfers.GetViewExport] = export,
            [Definitions.RouteIds.Transfers.GetViewExportArchive] = export,
            [Definitions.RouteIds.Transfers.StartViewImport] = import,
            [Definitions.RouteIds.Transfers.GetViewImport] = import,
        };
    }

    // The extension looks for functions in every assembly next to the one it renders, as the host does in its bin folder.
    // Next to the test copy sit the test platform assemblies, whose types do not load, so the copy in the API project's own
    // output is rendered instead; it is loaded apart, and everything it references resolves to the assemblies already loaded.
    private static Assembly LoadApiAssembly()
    {
        var testCopy = typeof(AccessHttp).Assembly;
        var path = Environment.GetEnvironmentVariable(AssemblyVariable);

        if (string.IsNullOrWhiteSpace(path))
        {
            // <project>/test/bin/<configuration>/<framework>/ -> <project>/src/bin/<configuration>/<framework>/
            var testOutput = new DirectoryInfo(Path.GetDirectoryName(testCopy.Location)!);
            var project = testOutput.Parent!.Parent!.Parent!.Parent!;
            path = Path.Combine(project.FullName, "src", "bin", testOutput.Parent.Name, testOutput.Name, Path.GetFileName(testCopy.Location));
        }

        File.Exists(path).ShouldBeTrue($"The API assembly to render was not found at {path}; set {AssemblyVariable}.");
        return new AssemblyLoadContext("storyteller-openapi").LoadFromAssemblyPath(path);
    }

    // The request the host would see from a local developer: http://localhost:7071/api.
    private sealed class LocalHostRequest : IHttpRequestDataObject
    {
        public string Scheme => "http";

        public HostString Host => new("localhost", 7071);

        public IQueryCollection Query => new QueryCollection();

        public IHeaderDictionary Headers => new HeaderDictionary();

        public Stream Body => Stream.Null;
    }
}
