namespace _42.Platform.Storyteller.Api;

public static class Definitions
{
    public static class Routes
    {
        public static class Access
        {
            public static class V1
            {
                public const string Account = $"{VERSION}/access/account";
                public const string AccessPoints = $"{VERSION}/access/points";
                public const string AccessPoint = $"{VERSION}/access/points/{{{Parameters.Key}}}";

                public const string Grant = $"{VERSION}/access/grant";
                public const string Revoke = $"{VERSION}/access/revoke";

                public const string Machines = $"{VERSION}/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/access/machines";
                public const string Machine = $"{VERSION}/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/access/machines/{{{Parameters.Id}}}";

                private const string VERSION = "v1";
            }
        }

        public static class Annotations
        {
            public static class V1
            {
                public const string Annotations = $"{VERSION}/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/annotations";
                public const string AnnotationsSimple = $"{VERSION}/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/annotations/simple";
                public const string Annotation = $"{VERSION}/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/annotations/{{{Parameters.Key}}}";
                public const string Descendants = $"{VERSION}/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/annotations/{{{Parameters.Key}}}/{{{Parameters.Descendants}}}";

                public const string Responsibilities = $"{VERSION}/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/responsibilities";
                public const string Subjects = $"{VERSION}/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/subjects";
                public const string Usages = $"{VERSION}/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/usages";
                public const string Contexts = $"{VERSION}/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/contexts";
                public const string Executions = $"{VERSION}/{{{Parameters.Organization}}}/{{{Parameters.Project}}}/{{{Parameters.View}}}/executions";

                private const string VERSION = "v1";
            }
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
        public const string Access = "Access";
        public const string Annotations = "Annotations";
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
