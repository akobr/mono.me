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

                public const string Machines = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/access/machines";
                public const string Machine = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/access/machines/{{{Parameters.Id}}}";
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
            }
        }

        public static class Configuration
        {
            public static class V1
            {
                public const string Configuration = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration/{{{Parameters.Key}}}";
                public const string ConfigurationResolved = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration/{{{Parameters.Key}}}/resolved";
                public const string Versions = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration/{{{Parameters.Key}}}/versions";
                public const string Version = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration/{{{Parameters.Key}}}/versions/{{{Parameters.Id}}}";
                public const string VersionChanges = $"v1/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/configuration/{{{Parameters.Key}}}/versions/{{{Parameters.Id}}}/changes";
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

            public const string GetMachineAccesses = nameof(GetMachineAccesses);
            public const string GetMachineAccess = nameof(GetMachineAccess);
            public const string CreateMachineAccess = nameof(CreateMachineAccess);

            public const string ResetMachineAccess = nameof(ResetMachineAccess);
            public const string DeleteMachineAccess = nameof(DeleteMachineAccess);
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
        }

        public static class Configuration
        {
            public const string GetConfiguration = nameof(GetConfiguration);
            public const string GetResolvedConfiguration = nameof(GetResolvedConfiguration);
            public const string SetConfiguration = nameof(SetConfiguration);
            public const string DeleteConfiguration = nameof(DeleteConfiguration);
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
    }

    public static class Tags
    {
        public const string Access = nameof(Access);
        public const string Annotations = nameof(Annotations);
        public const string Configuration = nameof(Configuration);
    }

    public static class Descriptions
    {
        public const string ResponseAccount = "Details about the log in account, contains all accessible access points.";
        public const string ResponseBadRequest = "The request is not well formed.";
        public const string ResponseUnauthorized = "Authentication or authorization issues. Scope(s): ";
        public const string ResponseInternalServerError = "Unexpected error occurred on the service.";

        public const string SecureManual = "Manually by token in Authorization HTTP header.";

        public const string Organization = "Target organization name.";
        public const string Project = "Target project name.";
        public const string View = "The target view inside the project.";
        public const string ContinuationToken = "The continuation token for multi-page queries.";
        public const string IdMachine = "The id of the machine access.";
        public const string Key = "The key of the requested annotation.";
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
        public const string Delete = "delete";
    }

    public static class ContentTypes
    {
        public const string Json = "application/json";
    }

    public static class SecuritySchemas
    {
        public const string Manual = "manual";
        public const string Integrated = "integrated";
    }

    public static class Others
    {
        public const string JWT = nameof(JWT);
    }
}
