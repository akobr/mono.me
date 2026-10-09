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

                public const string Responsibilities = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/responsibilities";
                public const string Subjects = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/subjects";
                public const string Usages = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/usages";
                public const string Contexts = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/contexts";
                public const string Executions = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/executions";
                public const string Units = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/units";
                public const string UnitsOfExecution = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/units-of-execution";
            }
        }

        public static class Configuration
        {
            public static class V1
            {
                public const string Configuration = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration/{{{Parameters.Key}}}";
                public const string ConfigurationResolved = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration/{{{Parameters.Key}}}/resolved";

                public const string Versions = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration/{{{Parameters.Key}}}/versions";
                public const string Version = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration/{{{Parameters.Key}}}/versions/{{{Parameters.Version}}}";
                public const string VersionDiff = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration/{{{Parameters.Key}}}/versions/{{{Parameters.Version}}}/diff";
                public const string VersionDiffCustom = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration/{{{Parameters.Key}}}/versions/{{{Parameters.Version}}}/diff/{{{Parameters.VersionFrom}}}";
                public const string ViewDiff = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration/{{{Parameters.Key}}}/diff/{{{Parameters.ViewTo}}}";
            }
        }

        public static class ConfigurationSchema
        {
            public static class V1
            {
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

        public static class Template
        {
            public static class V1
            {
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
            public const string SetMachineAuthentication = nameof(SetMachineAuthentication);

            public const string RenewMachineCertificate = nameof(RenewMachineCertificate);
            public const string GetSharedCertificates = nameof(GetSharedCertificates);
            public const string IssueSharedCertificate = nameof(IssueSharedCertificate);
            public const string RevokeSharedCertificate = nameof(RevokeSharedCertificate);

            public const string GetAuthConfiguration = nameof(GetAuthConfiguration);
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
        }

        public static class ConfigurationSchema
        {
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

        public static class Template
        {
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
        public const string Format = "format";
        public const string Force = "force";
    }

    public static class Tags
    {
        public const string Access = nameof(Access);
        public const string Annotations = nameof(Annotations);
        public const string Configurations = nameof(Configurations);
        public const string Schemas = nameof(Schemas);
        public const string Templates = nameof(Templates);
    }

    public static class Descriptions
    {
        public const string ResponseAccount = "Details about the log in account, contains all accessible access points.";
        public const string ResponseBadRequest = "The request is not well formed.";
        public const string ResponseUnauthorized = "Authentication issues: missing or invalid credentials, or a missing scope. Scope(s): ";
        public const string ResponseForbidden = "The caller is authenticated, but its role on the organization or project does not allow the operation.";
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
        public const string AnnotationType = "The annotation type code (e.g. rst, sbt, usg, cnt, exe, unt, uxe).";
        public const string DiffFormat = "Response format: 'json' (default) for structured hunk model, 'unified' for raw unified diff text.";
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
