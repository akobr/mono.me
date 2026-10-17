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

                public const string Versions = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration/{{{Parameters.Key}}}/versions";
                public const string Version = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration/{{{Parameters.Key}}}/versions/{{{Parameters.Version}}}";
                public const string VersionDiff = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration/{{{Parameters.Key}}}/versions/{{{Parameters.Version}}}/diff";
                public const string VersionDiffCustom = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration/{{{Parameters.Key}}}/versions/{{{Parameters.Version}}}/diff/{{{Parameters.VersionFrom}}}";
                public const string ViewDiff = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration/{{{Parameters.Key}}}/diff/{{{Parameters.ViewTo}}}";

                // Literal segments below "configurations" (plural); a single configuration lives below "configuration".
                public const string Exports = $"{Configurations}/exports";
                public const string Export = $"{Configurations}/exports/{{{Parameters.Id}}}";
                public const string ExportArchive = $"{Configurations}/exports/{{{Parameters.Id}}}/archive";
                public const string Imports = $"{Configurations}/imports";
                public const string Import = $"{Configurations}/imports/{{{Parameters.Id}}}";
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
            public const string SetConfiguration = nameof(SetConfiguration);
            public const string PatchConfiguration = nameof(PatchConfiguration);
            public const string DeleteConfiguration = nameof(DeleteConfiguration);

            public const string GetConfigurationVersions = nameof(GetConfigurationVersions);
            public const string GetConfigurationVersion = nameof(GetConfigurationVersion);
            public const string GetConfigurationVersionDiff = nameof(GetConfigurationVersionDiff);
            public const string GetConfigurationVersionDiffCustom = nameof(GetConfigurationVersionDiffCustom);
            public const string GetConfigurationViewDiff = nameof(GetConfigurationViewDiff);

            public const string StartConfigurationExport = nameof(StartConfigurationExport);
            public const string GetConfigurationExport = nameof(GetConfigurationExport);
            public const string GetConfigurationExportArchive = nameof(GetConfigurationExportArchive);
            public const string StartConfigurationImport = nameof(StartConfigurationImport);
            public const string GetConfigurationImport = nameof(GetConfigurationImport);
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
    }

    public static class Tags
    {
        public const string Access = nameof(Access);
        public const string Annotations = nameof(Annotations);
        public const string Configurations = nameof(Configurations);
        public const string Schemas = nameof(Schemas);
        public const string Templates = nameof(Templates);
        public const string KeyValues = nameof(KeyValues);
    }

    public static class Descriptions
    {
        public const string ResponseAccount = "Details about the log in account, contains all accessible access points.";
        public const string ResponseBadRequest = "The request is not well formed.";
        public const string ResponseUnauthorized = "Authentication issues: missing or invalid credentials, or a missing scope. Scope(s): ";
        public const string ResponseForbidden = "The caller is authenticated, but its role on the organization or project does not allow the operation (ErrorCode AccessDenied), or it is a machine restricted to an annotation that addresses another annotation or the whole project (ErrorCode AnnotationRestricted).";
        public const string ResponseConfigurationNotFound = "The configuration doesn't exist, or the annotation is deleted (ErrorCode AnnotationDeleted).";
        public const string ResponseConcurrentChange = "The annotations changed concurrently three times in a row (Conflict); try again.";
        public const string ResponsePreconditionFailed = "A JSON Patch test operation did not match the stored document (ErrorCode PatchTestFailed). Reload and retry.";
        public const string ResponseInternalServerError = "Unexpected error occurred on the service.";

        public const string SecureManual = "Manually by token in Authorization HTTP header.";

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
        public const string ConditionalResolvedRead = "A document with bindings is resolved for every read, so a 304 saves the transfer, not the evaluation; a document without bindings is not evaluated.";
        public const string ResponseNotModified = "The configuration is unchanged since the ETag sent in If-None-Match.";
        public const string EntityTag = "Weak entity tag of the configuration, for If-None-Match.";
        public const string AnnotationType = "The annotation type code (e.g. rst, sbt, usg, cnt, exe, unt, uxe).";
        public const string DiffFormat = "Response format: 'json' (default) for structured hunk model, 'unified' for raw unified diff text.";
        public const string KeyValueKey = "The key of the pair: dot-separated segments of letters, digits and underscores, case-insensitive, at most 128 characters.";
        public const string KeyValueVersion = "The version number of the pair.";
        public const string RecipientId = "The id of the recipient (the JWE kid).";
        public const string ResponseFailedDependency = "The customer's key (Byok) is not available: disabled, deleted or the role was removed (ErrorCode CustomerKeyUnavailable).";
        public const string ResponseNotImplemented = "The protection level is not configured in this deployment (ErrorCode ProtectionNotConfigured).";
        public const string ResponseServiceUnavailable = "The key-encryption key is not reachable right now (ErrorCode KeyEncryptionUnavailable); Retry-After says when to try again.";
        public const string ResponseMtlsRequired = "Secret rights are missing (ErrorCode AccessDenied), or the project delivers secrets to machines only over mTLS (ErrorCode MtlsRequired).";
        public const string NoStore = "Responses with a secret value are Cache-Control: no-store.";
        public const string IncludeInactive = "Also the annotations that are not active now: disabled (IsDisabled), scheduled (ValidFrom in the future) and expired (ExpiresAt passed). Default false: only active annotations.";
        public const string IncludeDeleted = "Also the soft-deleted annotations, each with its Deletion. Default false. A deleted annotation that is not active needs includeInactive too.";
        public const string IdExport = "The id of the export.";
        public const string IdImport = "The id of the import.";
        public const string ResponseTransferLimit = "The project already has the maximum of exports and imports in progress (TransferLimit).";
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
        public const string Zip = "application/zip";
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
