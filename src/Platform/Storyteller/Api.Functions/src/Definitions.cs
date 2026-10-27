using _42.Platform.Storyteller.Api.Security;

namespace _42.Platform.Storyteller.Api;

public static class Definitions
{
    public static class Routes
    {
        public static class Access
        {
            public static class V1
            {
                public const string Account = "v1/access/account";
                public const string AccessPoints = "v1/access/points";
                public const string AccessPoint = $"v1/access/points/{{{Parameters.Key}}}";

                // The caller's projects; "access" is a reserved organization name, so no organization route meets it.
                public const string Projects = "v1/access/projects";

                public const string Grant = "v1/access/grant";
                public const string Revoke = "v1/access/revoke";

                public const string Members = $"v1/access/points/{{{Parameters.Key}}}/members";
                public const string Member = $"v1/access/points/{{{Parameters.Key}}}/members/{{{Parameters.AccountId}}}";

                public const string PointInvitations = $"v1/access/points/{{{Parameters.Key}}}/invitations";
                public const string PointInvitation = $"v1/access/points/{{{Parameters.Key}}}/invitations/{{{Parameters.Id}}}";
                public const string PointInvitationResend = $"v1/access/points/{{{Parameters.Key}}}/invitations/{{{Parameters.Id}}}/resend";
                public const string MyInvitations = "v1/access/invitations/mine";
                public const string InvitationAccept = $"v1/access/invitations/{{{Parameters.Id}}}/accept";
                public const string InvitationDecline = $"v1/access/invitations/{{{Parameters.Id}}}/decline";

                public const string Machines = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/access/machines";
                public const string Machine = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/access/machines/{{{Parameters.Id}}}";

                public const string CertificateAuthority = "v1/access/certificate-authority";
                public const string MachineAuthentication = $"v1/access/points/{{{Parameters.Key}}}/machine-authentication";

                public const string MachineCertificateRenew = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/access/machines/{{{Parameters.Id}}}/certificate/renew";
                public const string MachineFederationRules = $"{Machine}/federation-rules";

                // Workload identity federation: the project's trusted issuers and its RFC 8693 token endpoint.
                public const string FederatedIssuers = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/access/issuers";
                public const string FederatedIssuer = $"{FederatedIssuers}/{{{Parameters.Name}}}";
                public const string MachineToken = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/access/token";
                public const string SharedCertificates = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/access/certificates";
                public const string SharedCertificate = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/access/certificates/{{{Parameters.Thumbprint}}}";
            }
        }

        public static class Auth
        {
            public static class V1
            {
                public const string Configuration = "v1/auth/configuration";
            }
        }

        public static class Annotations
        {
            public static class V1
            {
                public const string Annotations = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/annotations";
                public const string AnnotationsSimple = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/annotations/simple";
                public const string Annotation = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/annotations/{{{Parameters.Key}}}";
                public const string Descendants = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/annotations/{{{Parameters.Key}}}/{{{Parameters.Descendants}}}";
                public const string History = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/annotations/{{{Parameters.Key}}}/history";

                // Literal segments win over {key}; an annotation key always starts with a type code and a dot.
                public const string Deleted = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/annotations/deleted";
                public const string DeletedAnnotation = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/annotations/deleted/{{{Parameters.Key}}}";
                public const string DeletedLookup = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/annotations/deleted/lookup";
                public const string DeletedRestore = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/annotations/deleted/restore";

                public const string Responsibilities = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/responsibilities";
                public const string Subjects = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/subjects";
                public const string Usages = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/usages";
                public const string Contexts = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/contexts";
                public const string Executions = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/executions";
                public const string Units = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/units";
                public const string UnitsOfExecution = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/units-of-execution";
            }
        }

        public static class Bindings
        {
            public static class V1
            {
                public const string Bindings = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/bindings";
            }
        }

        public static class Views
        {
            public static class V1
            {
                public const string Views = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/views";
                public const string View = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/views/{{{Parameters.View}}}";
            }
        }

        public static class Configuration
        {
            public static class V1
            {
                public const string Configurations = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configurations";
                public const string Configuration = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration/{{{Parameters.Key}}}";
                public const string ConfigurationResolved = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration/{{{Parameters.Key}}}/resolved";

                // Under the configuration's own path, so a client that invalidates the configuration covers it too.
                public const string ConfigurationComposition = $"{Configuration}/composition";

                public const string Versions = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration/{{{Parameters.Key}}}/versions";
                public const string Version = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration/{{{Parameters.Key}}}/versions/{{{Parameters.Version}}}";
                public const string VersionDiff = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration/{{{Parameters.Key}}}/versions/{{{Parameters.Version}}}/diff";
                public const string VersionDiffCustom = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration/{{{Parameters.Key}}}/versions/{{{Parameters.Version}}}/diff/{{{Parameters.VersionFrom}}}";
                public const string ViewDiff = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration/{{{Parameters.Key}}}/diff/{{{Parameters.ViewTo}}}";
            }
        }

        public static class Transfers
        {
            public static class V1
            {
                // Literal segments after the view; "views" is a reserved view name, so they never meet the view routes.
                public const string Exports = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/exports";
                public const string Export = $"{Exports}/{{{Parameters.Id}}}";
                public const string ExportArchive = $"{Exports}/{{{Parameters.Id}}}/archive";
                public const string Imports = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/imports";
                public const string Import = $"{Imports}/{{{Parameters.Id}}}";
            }
        }

        public static class ConfigurationSchema
        {
            public static class V1
            {
                public const string Schemas = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration-schemas";
                public const string SchemaType = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration-schema/type/{{{Parameters.AnnotationType}}}";
                public const string SchemaAnnotation = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration-schema/{{{Parameters.Key}}}";
                public const string SchemaDescendantType = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration-schema/{{{Parameters.Key}}}/type/{{{Parameters.AnnotationType}}}";
                public const string SchemaCombined = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration-schema/{{{Parameters.Key}}}/definition";

                public const string SchemaTypeVersions = $"{SchemaType}/versions";
                public const string SchemaTypeVersion = $"{SchemaType}/versions/{{{Parameters.Version}}}";
                public const string SchemaTypeVersionDiff = $"{SchemaTypeVersion}/diff";
                public const string SchemaTypeVersionDiffCustom = $"{SchemaTypeVersionDiff}/{{{Parameters.VersionFrom}}}";

                public const string SchemaAnnotationVersions = $"{SchemaAnnotation}/versions";
                public const string SchemaAnnotationVersion = $"{SchemaAnnotation}/versions/{{{Parameters.Version}}}";
                public const string SchemaAnnotationVersionDiff = $"{SchemaAnnotationVersion}/diff";
                public const string SchemaAnnotationVersionDiffCustom = $"{SchemaAnnotationVersionDiff}/{{{Parameters.VersionFrom}}}";

                public const string SchemaDescendantTypeVersions = $"{SchemaDescendantType}/versions";
                public const string SchemaDescendantTypeVersion = $"{SchemaDescendantType}/versions/{{{Parameters.Version}}}";
                public const string SchemaDescendantTypeVersionDiff = $"{SchemaDescendantTypeVersion}/diff";
                public const string SchemaDescendantTypeVersionDiffCustom = $"{SchemaDescendantTypeVersionDiff}/{{{Parameters.VersionFrom}}}";
            }
        }

        public static class KeyValues
        {
            public static class V1
            {
                // View-scoped: the pairs of a view. The collection actions are POST only, and a key is never addressed by POST,
                // so a key named "restore" or "export" still reaches the key functions with GET, PUT and DELETE.
                public const string KeyValues = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/key-values";
                public const string KeyValue = $"{KeyValues}/{{{Parameters.Key}}}";
                public const string Restore = $"{KeyValues}/restore";
                public const string Purge = $"{KeyValues}/purge";
                public const string Protect = $"{KeyValues}/protect";
                public const string Export = $"{KeyValues}/export";
                public const string Import = $"{KeyValues}/import";

                public const string Versions = $"{KeyValue}/versions";
                public const string Version = $"{Versions}/{{{Parameters.Version}}}";
                public const string VersionDiff = $"{Version}/diff";
                public const string VersionDiffCustom = $"{VersionDiff}/{{{Parameters.VersionFrom}}}";

                // Project-scoped: the literal "key-values" takes the place of the view, so "key-values" is a reserved view name.
                public const string ProjectKeyValues = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/key-values";
                public const string Policy = $"{ProjectKeyValues}/policy";
                public const string ByokSetup = $"{Policy}/byok/setup";
                public const string ByokVerify = $"{Policy}/byok/verify";
                public const string Rotate = $"{Policy}/rotate";
                public const string Recipients = $"{ProjectKeyValues}/recipients";
                public const string Recipient = $"{Recipients}/{{{Parameters.Id}}}";
                public const string Audit = $"{ProjectKeyValues}/audit";
                public const string Usage = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/usage/key-values";
            }
        }

        public static class Template
        {
            public static class V1
            {
                public const string Templates = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/templates";
                public const string Template = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/template/{{{Parameters.AnnotationType}}}";

                public const string Versions = $"{Template}/versions";
                public const string Version = $"{Template}/versions/{{{Parameters.Version}}}";
                public const string VersionDiff = $"{Template}/versions/{{{Parameters.Version}}}/diff";
                public const string VersionDiffCustom = $"{Template}/versions/{{{Parameters.Version}}}/diff/{{{Parameters.VersionFrom}}}";
            }
        }
    }

    public static class RouteIds
    {
        public static class Access
        {
            public const string GetAccount = nameof(GetAccount);
            public const string CreateAccount = nameof(CreateAccount);

            public const string GetAccessPoints = nameof(GetAccessPoints);
            public const string GetAccessPoint = nameof(GetAccessPoint);
            public const string CreateAccessPoint = nameof(CreateAccessPoint);
            public const string UpdateAccessPoint = nameof(UpdateAccessPoint);
            public const string GetProjects = nameof(GetProjects);

            public const string GrantUserAccess = nameof(GrantUserAccess);
            public const string RevokeUserAccess = nameof(RevokeUserAccess);

            public const string GetMembers = nameof(GetMembers);
            public const string SetMemberRole = nameof(SetMemberRole);
            public const string RemoveMember = nameof(RemoveMember);

            public const string GetInvitations = nameof(GetInvitations);
            public const string CreateInvitation = nameof(CreateInvitation);
            public const string ResendInvitation = nameof(ResendInvitation);
            public const string RevokeInvitation = nameof(RevokeInvitation);
            public const string GetMyInvitations = nameof(GetMyInvitations);
            public const string AcceptInvitation = nameof(AcceptInvitation);
            public const string DeclineInvitation = nameof(DeclineInvitation);

            public const string GetMachineAccesses = nameof(GetMachineAccesses);
            public const string GetMachineAccess = nameof(GetMachineAccess);
            public const string CreateMachineAccess = nameof(CreateMachineAccess);

            public const string ResetMachineAccess = nameof(ResetMachineAccess);
            public const string DeleteMachineAccess = nameof(DeleteMachineAccess);

            public const string GetCertificateAuthority = nameof(GetCertificateAuthority);
            public const string GetMachineAuthentication = nameof(GetMachineAuthentication);
            public const string SetMachineAuthentication = nameof(SetMachineAuthentication);

            public const string RenewMachineCertificate = nameof(RenewMachineCertificate);
            public const string GetSharedCertificates = nameof(GetSharedCertificates);
            public const string IssueSharedCertificate = nameof(IssueSharedCertificate);
            public const string RevokeSharedCertificate = nameof(RevokeSharedCertificate);

            public const string GetFederatedIssuers = nameof(GetFederatedIssuers);
            public const string GetFederatedIssuer = nameof(GetFederatedIssuer);
            public const string SetFederatedIssuer = nameof(SetFederatedIssuer);
            public const string DeleteFederatedIssuer = nameof(DeleteFederatedIssuer);
            public const string SetMachineFederationRules = nameof(SetMachineFederationRules);
            public const string ExchangeMachineToken = nameof(ExchangeMachineToken);

            public const string GetAuthConfiguration = nameof(GetAuthConfiguration);
        }

        public static class Bindings
        {
            public const string GetBindingCatalog = nameof(GetBindingCatalog);
        }

        public static class Views
        {
            public const string GetViews = nameof(GetViews);
            public const string CreateView = nameof(CreateView);
            public const string UpdateView = nameof(UpdateView);
        }

        public static class Annotations
        {
            public const string GetAnnotations = nameof(GetAnnotations);
            public const string SetAnnotations = nameof(SetAnnotations);
            public const string SetAnnotationsSimple = nameof(SetAnnotationsSimple);

            public const string GetAnnotation = nameof(GetAnnotation);
            public const string SetAnnotation = nameof(SetAnnotation);
            public const string DeleteAnnotation = nameof(DeleteAnnotation);
            public const string GetDescendants = nameof(GetDescendants);
            public const string GetAnnotationHistory = nameof(GetAnnotationHistory);

            public const string GetDeletedAnnotations = nameof(GetDeletedAnnotations);
            public const string GetDeletedAnnotation = nameof(GetDeletedAnnotation);
            public const string FindDeletedAnnotations = nameof(FindDeletedAnnotations);
            public const string RestoreAnnotations = nameof(RestoreAnnotations);
            public const string PurgeAnnotation = nameof(PurgeAnnotation);

            public const string GetResponsibilities = nameof(GetResponsibilities);
            public const string GetSubjects = nameof(GetSubjects);
            public const string GetUsages = nameof(GetUsages);
            public const string GetContexts = nameof(GetContexts);
            public const string GetExecutions = nameof(GetExecutions);
            public const string GetUnits = nameof(GetUnits);
            public const string GetUnitsOfExecution = nameof(GetUnitsOfExecution);
        }

        public static class Configuration
        {
            public const string GetConfigurations = nameof(GetConfigurations);
            public const string GetConfiguration = nameof(GetConfiguration);
            public const string GetResolvedConfiguration = nameof(GetResolvedConfiguration);
            public const string GetConfigurationComposition = nameof(GetConfigurationComposition);
            public const string SetConfiguration = nameof(SetConfiguration);
            public const string PatchConfiguration = nameof(PatchConfiguration);
            public const string DeleteConfiguration = nameof(DeleteConfiguration);

            public const string GetConfigurationVersions = nameof(GetConfigurationVersions);
            public const string GetConfigurationVersion = nameof(GetConfigurationVersion);
            public const string GetConfigurationVersionDiff = nameof(GetConfigurationVersionDiff);
            public const string GetConfigurationVersionDiffCustom = nameof(GetConfigurationVersionDiffCustom);
            public const string GetConfigurationViewDiff = nameof(GetConfigurationViewDiff);
        }

        public static class Transfers
        {
            public const string StartViewExport = nameof(StartViewExport);
            public const string GetViewExport = nameof(GetViewExport);
            public const string GetViewExportArchive = nameof(GetViewExportArchive);
            public const string StartViewImport = nameof(StartViewImport);
            public const string GetViewImport = nameof(GetViewImport);
        }

        public static class ConfigurationSchema
        {
            public const string GetConfigurationSchemas = nameof(GetConfigurationSchemas);
            public const string GetConfigurationSchema = nameof(GetConfigurationSchema);
            public const string SetConfigurationSchema = nameof(SetConfigurationSchema);
            public const string DeleteConfigurationSchema = nameof(DeleteConfigurationSchema);

            public const string GetAnnotationSchema = nameof(GetAnnotationSchema);
            public const string SetAnnotationSchema = nameof(SetAnnotationSchema);
            public const string DeleteAnnotationSchema = nameof(DeleteAnnotationSchema);

            public const string GetDescendantTypeSchema = nameof(GetDescendantTypeSchema);
            public const string SetDescendantTypeSchema = nameof(SetDescendantTypeSchema);
            public const string DeleteDescendantTypeSchema = nameof(DeleteDescendantTypeSchema);

            public const string GetCombinedConfigurationSchema = nameof(GetCombinedConfigurationSchema);

            public const string GetSchemaVersions = nameof(GetSchemaVersions);
            public const string GetSchemaVersion = nameof(GetSchemaVersion);
            public const string GetSchemaVersionDiff = nameof(GetSchemaVersionDiff);
            public const string GetSchemaVersionDiffCustom = nameof(GetSchemaVersionDiffCustom);

            public const string GetAnnotationSchemaVersions = nameof(GetAnnotationSchemaVersions);
            public const string GetAnnotationSchemaVersion = nameof(GetAnnotationSchemaVersion);
            public const string GetAnnotationSchemaVersionDiff = nameof(GetAnnotationSchemaVersionDiff);
            public const string GetAnnotationSchemaVersionDiffCustom = nameof(GetAnnotationSchemaVersionDiffCustom);

            public const string GetDescendantTypeSchemaVersions = nameof(GetDescendantTypeSchemaVersions);
            public const string GetDescendantTypeSchemaVersion = nameof(GetDescendantTypeSchemaVersion);
            public const string GetDescendantTypeSchemaVersionDiff = nameof(GetDescendantTypeSchemaVersionDiff);
            public const string GetDescendantTypeSchemaVersionDiffCustom = nameof(GetDescendantTypeSchemaVersionDiffCustom);
        }

        public static class KeyValues
        {
            public const string GetKeyValues = nameof(GetKeyValues);
            public const string GetKeyValue = nameof(GetKeyValue);
            public const string SetKeyValue = nameof(SetKeyValue);
            public const string DeleteKeyValue = nameof(DeleteKeyValue);
            public const string RestoreKeyValues = nameof(RestoreKeyValues);
            public const string PurgeKeyValues = nameof(PurgeKeyValues);
            public const string ProtectKeyValues = nameof(ProtectKeyValues);

            public const string GetKeyValueVersions = nameof(GetKeyValueVersions);
            public const string GetKeyValueVersion = nameof(GetKeyValueVersion);
            public const string GetKeyValueVersionDiff = nameof(GetKeyValueVersionDiff);
            public const string GetKeyValueVersionDiffCustom = nameof(GetKeyValueVersionDiffCustom);

            public const string ExportKeyValues = nameof(ExportKeyValues);
            public const string ImportKeyValues = nameof(ImportKeyValues);

            public const string GetKeyValuePolicy = nameof(GetKeyValuePolicy);
            public const string SetKeyValuePolicy = nameof(SetKeyValuePolicy);
            public const string GetByokSetup = nameof(GetByokSetup);
            public const string VerifyByok = nameof(VerifyByok);
            public const string RotateKeyValueKey = nameof(RotateKeyValueKey);

            public const string GetRecipients = nameof(GetRecipients);
            public const string AddRecipient = nameof(AddRecipient);
            public const string UpdateRecipient = nameof(UpdateRecipient);
            public const string RemoveRecipient = nameof(RemoveRecipient);

            public const string GetKeyValueAudit = nameof(GetKeyValueAudit);
            public const string GetKeyValueUsage = nameof(GetKeyValueUsage);
        }

        public static class Template
        {
            public const string GetTemplates = nameof(GetTemplates);
            public const string GetTemplate = nameof(GetTemplate);
            public const string SetTemplate = nameof(SetTemplate);
            public const string PatchTemplate = nameof(PatchTemplate);
            public const string DeleteTemplate = nameof(DeleteTemplate);

            public const string GetTemplateVersions = nameof(GetTemplateVersions);
            public const string GetTemplateVersion = nameof(GetTemplateVersion);
            public const string GetTemplateVersionDiff = nameof(GetTemplateVersionDiff);
            public const string GetTemplateVersionDiffCustom = nameof(GetTemplateVersionDiffCustom);
        }
    }

    public static class Parameters
    {
        public const string Organization = "organization";
        public const string Project = "project";
        public const string View = "view";
        public const string Id = "id";
        public const string Key = "key";
        public const string NameQuery = "nameQuery";
        public const string ContinuationToken = "continuationToken";
        public const string Descendants = "descendants";
        public const string Version = "version";
        public const string VersionFrom = "versionFrom";
        public const string ViewTo = "viewTo";
        public const string AnnotationType = "annotationType";
        public const string Thumbprint = "thumbprint";
        public const string AccountId = "accountId";
        public const string Discover = "discover";
        public const string KeyPrefix = "keyPrefix";
        public const string Format = "format";
        public const string Force = "force";
        public const string Roots = "roots";
        public const string DeletionId = "deletionId";
        public const string Protection = "protection";
        public const string State = "state";
        public const string IncludeValues = "includeValues";
        public const string Reveal = "reveal";
        public const string IncludeDeleted = "includeDeleted";
        public const string Permanent = "permanent";
        public const string ExpectedVersion = "expectedVersion";
        public const string Mode = "mode";
        public const string DryRun = "dryRun";
        public const string Operation = "operation";
        public const string From = "from";
        public const string To = "to";
        public const string IncludeInactive = "includeInactive";
        public const string FileName = "fileName";
        public const string Include = "include";
        public const string Existing = "existing";
        public const string Name = "name";

        // A request header; HeaderNames.IfNoneMatch is not a constant, so attributes cannot use it.
        public const string IfNoneMatch = "If-None-Match";
    }

    public static class Tags
    {
        public const string Access = nameof(Access);
        public const string Annotations = nameof(Annotations);
        public const string Configurations = nameof(Configurations);
        public const string Schemas = nameof(Schemas);
        public const string Templates = nameof(Templates);
        public const string KeyValues = nameof(KeyValues);
        public const string Transfers = nameof(Transfers);
    }

    public static class Descriptions
    {
        public const string ResponseAccount = "Details about the log in account, contains all accessible access points.";
        public const string ResponseBadRequest = "The request is not well formed.";
        public const string ResponseBadRequestDescendantType = "The request is not well formed, or the descendant type cannot descend from the annotation key (ErrorCode InvalidKey).";
        public const string ResponseBadRequestMachineAccessCreate = "The annotation key is not valid (ErrorCode InvalidKey), a scope doesn't exist, the scopes have no runtime scope, or the credential kind is not supported or its identity provider cannot carry one of the scopes.";

        // The 401 of the routes machines and users call. Data scopes are no part of it: a machine without the scopes of
        // the operation is authenticated and gets 403.
        public const string ResponseUnauthorized = "Authentication issues: missing or invalid credentials, a user token without " + Scopes.User.Impersonation + ", or a machine the project does not know or whose credential kind it does not enable.";

        // The 401 and 403 of the routes for users only: machines are refused whatever their credentials carry.
        public const string ResponseUnauthorizedUserOnly = "Authentication issues: missing or invalid credentials, or a token that does not act for a user (the scope " + Scopes.User.Impersonation + " is missing).";
        public const string ResponseForbiddenUserOnly = "Only users may call this operation: a machine is refused (ErrorCode AccessDenied), and so is a user whose role on the organization or project does not allow it (ErrorCode AccessDenied).";

        public const string ResponseForbidden = "The caller is authenticated, but its role on the organization or project does not allow the operation (ErrorCode AccessDenied), or it is a machine restricted to an annotation that addresses another annotation or the whole project (ErrorCode AnnotationRestricted).";

        // The 403 of the routes machines call: a machine needs the scopes of the operation, stored on it and carried by
        // its credentials, a management operation its runtime scope and its management scope; users need only the role.
        public const string ResponseForbiddenAnnotationRead = ResponseForbidden + MachineNeedsScope + Scopes.Annotation.Read + MachineDenied;
        public const string ResponseForbiddenAnnotationWrite = ResponseForbidden + MachineNeedsScope + Scopes.Annotation.Write + MachineDenied;
        public const string ResponseForbiddenManagementAnnotationManage = ResponseForbidden + MachineNeedsScopes + Scopes.Annotation.Write + " and " + Scopes.Annotation.Manage + MachineDenied;
        public const string ResponseForbiddenConfigurationRead = ResponseForbidden + MachineNeedsScope + Scopes.Configuration.Read + MachineDenied;
        public const string ResponseForbiddenConfigurationWrite = ResponseForbidden + MachineNeedsScope + Scopes.Configuration.Write + MachineDenied;
        public const string ResponseForbiddenManagementSchemaWrite = ResponseForbidden + MachineNeedsScopes + Scopes.Configuration.Write + " and " + Scopes.Schema.Write + MachineDenied;
        public const string ResponseForbiddenManagementTemplateWrite = ResponseForbidden + MachineNeedsScopes + Scopes.Configuration.Write + " and " + Scopes.Template.Write + MachineDenied;
        public const string ResponseForbiddenManagementKeyValueManage = ResponseForbidden + MachineNeedsScopes + Scopes.Configuration.Write + " and " + Scopes.KeyValue.Manage + " (AccessDenied); restoring a secret key also needs " + Scopes.Secrets.Write + ".";

        // Project metadata: every machine of the project reads it, also one restricted to an annotation.
        public const string ResponseForbiddenViews = MetadataForbidden + " A machine needs one of the scopes " + Scopes.Annotation.Read + " or " + Scopes.Configuration.Read + " (AccessDenied); discovering the views (discover=true) is for users only.";
        public const string ResponseForbiddenBindingCatalog = MetadataForbidden + MachineNeedsScope + Scopes.Configuration.Read + MachineDenied;

        // Create, reset and the federation rules: a machine's scopes may not exceed what its creator may do or grant.
        public const string ResponseForbiddenMachineScopes = ResponseForbiddenUserOnly + " Creating a machine, resetting it, replacing its federation rules or deleting it needs the Contributor role, ContributorWithSecrets when the machine has a Secrets scope and Administrator when it has a management scope (ErrorCode AccessDenied).";
        public const string ResponseConfigurationNotFound = "The configuration doesn't exist, or the annotation is deleted (ErrorCode AnnotationDeleted).";
        public const string ResponseConcurrentChange = "The annotations changed concurrently three times in a row (Conflict); try again.";
        public const string ResponsePreconditionFailed = "A JSON Patch test operation did not match the stored document (ErrorCode PatchTestFailed). Reload and retry.";
        public const string ResponseInternalServerError = "Unexpected error occurred on the service.";

        // The security schemes. Whether a machine's kind is accepted also depends on the project's machine authentication
        // policy, which the requirements of an operation cannot say.
        public const string SecureManual = "A bearer token in the Authorization header: a user's token, a client-credentials token of the identity provider, or the Storyteller token a federated machine gets from ExchangeMachineToken.";
        public const string SecureApiKey = "A machine's API key in the Authorization header, the scheme included in the value: 'ApiKey 2s.<base64url(organization:project:machineAccessId)>.<secret>'. The ApiKey and CertificateAndApiKey kinds, when the project's machine authentication policy enables them.";
        public const string SecureMtls = "The machine's client certificate of the TLS handshake. The platform's front end passes it to the API in X-ARR-ClientCert; a client never sets the header itself, except against a local development host that allows it. The Certificate and CertificateAndApiKey kinds, when the project's machine authentication policy enables them. A shared certificate needs apiKey as well, and the policy can require a certificate for secret reads (ErrorCode MtlsRequired).";

        public const string Organization = "Target organization name.";
        public const string Project = "Target project name.";
        public const string View = "The target view inside the project.";
        public const string ContinuationToken = "The continuation token for multi-page queries.";
        public const string IdMachine = "The id of the machine access.";
        public const string AccessPointKey = "The key of the access point: organization, or organization.project.";
        public const string AccountId = "The id of the account (the identity provider's subject).";
        public const string InvitationId = "The id of the invitation.";
        public const string Key = "The key of the requested annotation.";
        public const string ConditionalRead = "The response carries a weak ETag. Send it back in If-None-Match to get 304 Not Modified without a body while the configuration stays the same.";
        public const string ConditionalResolvedRead = "A document without bindings is not evaluated. A document with bindings is revalidated from the versions of the key/value pairs and annotations it read, without evaluating it, when the serving instance recorded them for the sent ETag; otherwise it is evaluated and may still answer 304. JSON-e, and Key Vault secrets for a caller that may read secrets, are evaluated for every read.";
        public const string ConfigurationComposition = "How the effective configuration is composed, read from the stored documents. Sources lists every merged document once: the configurations of the annotation and its ancestors (Missing when there is none) and the view's templates of their types. Nodes are the calculations of the inheritance graph in the order they finish, the annotation last; a shared ancestor is calculated once per branch, so it has a node per branch. Steps are the merges of the sources in the order of application, and Origins give the step that set each value of Content, by RFC 6901 pointer. Content and Hash are those of the effective read while its cache matches the stored documents. Bindings are not evaluated. The read shows the ancestors' documents and the templates in full, so it needs the whole project: a machine restricted to an annotation is refused (ErrorCode AnnotationRestricted), even for its own annotation.";
        public const string ResponseNotModified = "The configuration is unchanged since the ETag sent in If-None-Match.";
        public const string EntityTag = "Weak entity tag of the configuration, for If-None-Match.";
        public const string IfNoneMatch = "One or more entity tags of earlier reads, or *. The comparison is weak. A header that is not a valid list of entity tags is ignored, and the read answers 200.";
        public const string CacheControlConfigurationRead = "private, no-cache: shared caches do not store the response, and a client revalidates its copy before each use.";
        public const string CacheControlResolvedRead = "no-store when the caller may read secrets, otherwise private, no-cache.";
        public const string SecretsWithheld = "mtls-required when the project delivers secrets to machines only over mTLS and a machine that may read secrets called without its client certificate; the secrets then stay unbound, as their @ expressions. Absent otherwise.";

        // ?format= of the effective and resolved reads (configuration-formats.md, API).
        public const string ConfigurationFormat = "The representation of the body, in any casing. json, the default (also when empty), is the Configuration model. flat, properties, env and yaml are its Content alone, without the envelope: a JSON object of IConfiguration keys and string values, Spring Boot properties, a .env file or YAML. Each format has its own ETag. Any other value, or the parameter repeated, answers 400 (ErrorCode InvalidQuery).";
        public const string ConfigurationFormatBody = "With format=flat the body is a JSON object of strings (application/json), with properties or env text/plain, with yaml application/yaml.";
        public const string ResponseConfigurationFormatInvalid = "The configuration cannot be written in the requested format (ErrorCode format.<rule>: format.not-an-object, format.empty-name, format.colon-in-name, format.case-collision, format.bracket-in-name, format.array-gap or format.invalid-env-name). Details hold the Format, the Rule and the JSON Path; the message names the path, never a value.";
        public const string ResponseResolvedConfigurationInvalid = "A binding in the configuration cannot be evaluated or exceeds an evaluation limit (ErrorCode binding.evaluation or binding.limit.<kind>), or the resolved configuration cannot be written in the requested format (ErrorCode format.<rule>, with Details as on the effective read).";
        public const string AnnotationType = "The annotation type code (e.g. rst, sbt, usg, cnt, exe, unt, uxe).";
        public const string DiffFormat = "Response format: 'json' (default) for structured hunk model, 'unified' for raw unified diff text.";
        public const string KeyValueKey = "The key of the pair: dot-separated segments of letters, digits and underscores, case-insensitive, at most 128 characters.";
        public const string KeyValueVersion = "The version number of the pair.";
        public const string RecipientId = "The id of the recipient (the JWE kid).";
        public const string ResponseFailedDependency = "The customer's key (Byok) is not available: disabled, deleted or the role was removed (ErrorCode CustomerKeyUnavailable).";
        public const string ResponseNotImplemented = "The protection level is not configured in this deployment (ErrorCode ProtectionNotConfigured).";
        public const string ResponseServiceUnavailable = "The key-encryption key is not reachable right now (ErrorCode KeyEncryptionUnavailable); Retry-After says when to try again.";
        public const string ResponseMtlsRequired = "Secret rights are missing (ErrorCode AccessDenied), the project delivers secrets to machines only over mTLS (ErrorCode MtlsRequired), or the machine is restricted to an annotation (ErrorCode AnnotationRestricted).";

        // The 403 of the key/value routes with secret values: the secret rights and the mTLS rule, and the machine scopes.
        public const string ResponseMtlsRequiredConfigurationRead = ResponseMtlsRequired + MachineNeedsScope + Scopes.Configuration.Read + ", and " + Scopes.Secrets.Read + " for secret values" + MachineDenied;
        public const string ResponseMtlsRequiredConfigurationWrite = ResponseMtlsRequired + MachineNeedsScope + Scopes.Configuration.Write + ", and " + Scopes.Secrets.Write + " for secret levels" + MachineDenied;
        public const string ResponseMtlsRequiredDeleteKeyValue = ResponseMtlsRequired + MachineNeedsScope + Scopes.Configuration.Write + ", and " + Scopes.Secrets.Write + " for a secret key" + MachineDenied + " A permanent delete is for users with the Administrator role only.";
        public const string ResponseMtlsRequiredKeyValueManage = ResponseMtlsRequired + MachineNeedsScopes + Scopes.Configuration.Write + ", " + Scopes.KeyValue.Manage + " and " + Scopes.Secrets.Write + MachineDenied;
        public const string ResponseMtlsRequiredKeyValueExport = ResponseMtlsRequired + MachineNeedsScopes + Scopes.Configuration.Read + " and " + Scopes.Configuration.Transfer + ", and " + Scopes.Secrets.Read + " to export secrets" + MachineDenied;
        public const string ResponseMtlsRequiredKeyValueImport = ResponseMtlsRequired + MachineNeedsScopes + Scopes.Configuration.Write + " and " + Scopes.Configuration.Transfer + ", and " + Scopes.Secrets.Write + " for secret items" + MachineDenied;
        public const string NoStore = "Responses with a secret value are Cache-Control: no-store.";
        public const string IncludeInactive = "Also the annotations that are not active now: disabled (IsDisabled), scheduled (ValidFrom in the future) and expired (ExpiresAt passed). Default false: only active annotations.";
        public const string IncludeDeleted = "Also the soft-deleted annotations, each with its Deletion. Default false. A deleted annotation that is not active needs includeInactive too.";
        public const string IdExport = "The id of the export.";
        public const string IdImport = "The id of the import.";
        public const string ResponseTransferLimit = "The project already has the maximum of exports and imports in progress (TransferLimit).";
        public const string ResponseTransferForbiddenExport = TransferForbidden + MachineNeedsTransfer + "the read scope of every included kind (" + Scopes.Annotation.Read + " for annotations, " + Scopes.Configuration.Read + " for schemas, templates and configurations); reading an export needs the scopes of the kinds it includes. " + TransferForbiddenNames;
        public const string ResponseTransferForbiddenImport = TransferForbidden + MachineNeedsTransfer + "the scopes of the direct writes of every included kind (" + Scopes.Annotation.Write + " and " + Scopes.Annotation.Manage + " for annotations, " + Scopes.Configuration.Write + " and " + Scopes.Schema.Write + " for schemas, " + Scopes.Configuration.Write + " and " + Scopes.Template.Write + " for templates and " + Scopes.Configuration.Write + " for configurations); reading an import needs the scopes of the kinds it includes. " + TransferForbiddenNames;
        public const string TransferInclude = "The kinds to import, comma-separated and case-insensitive: Annotations, Schemas, Templates, Configurations. Empty means all four. The files of the other kinds are counted as excluded.";
        public const string TransferExisting = "What happens to a document the view already has: Overwrite (default) writes the file as a new version, updates an annotation and re-creates a deleted one; Skip keeps the stored document. An equal document is Unchanged either way.";
        public const string FederatedIssuerName = "The name of the trusted issuer: 1 to 63 lower-case letters, digits or hyphens.";

        // The parts the 403 descriptions above are built from.
        private const string MachineNeedsScope = " A machine needs the scope ";
        private const string MachineNeedsScopes = " A machine needs the scopes ";
        private const string MachineDenied = " (AccessDenied).";
        private const string MetadataForbidden = "The caller is authenticated, but its role on the project does not allow the operation (ErrorCode AccessDenied).";
        private const string MachineNeedsTransfer = " A machine needs no role but the scope " + Scopes.Configuration.Transfer + " and ";
        private const string TransferForbidden = "The user's role on the project is below Administrator, or a scope of the machine is missing (AccessDenied), or the machine is restricted to an annotation (AnnotationRestricted).";
        private const string TransferForbiddenNames = "Users need no data scopes, only the Administrator role. The message names the missing role or scope.";
    }

    public static class Errors
    {
        public const string InvalidMachineId = "Invalid machine id.";
    }

    public static class Methods
    {
        public const string Get = "get";
        public const string Post = "post";
        public const string Put = "put";
        public const string Patch = "patch";
        public const string Delete = "delete";
    }

    public static class ContentTypes
    {
        public const string Json = "application/json";
        public const string PlainText = "text/plain";
        public const string Yaml = "application/yaml";
        public const string Zip = "application/zip";
        public const string FormUrlEncoded = "application/x-www-form-urlencoded";
    }

    public static class SecuritySchemas
    {
        public const string Manual = "manual";
        public const string Integrated = "integrated";
        public const string ApiKey = "apiKey";
        public const string Mtls = "mtls";
    }

    public static class Others
    {
        public const string JWT = nameof(JWT);
    }
}
