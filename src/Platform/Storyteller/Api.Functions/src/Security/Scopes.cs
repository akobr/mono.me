using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller.Api.Security;

// The scope strings that claims and tokens carry, as constants for the OpenAPI descriptions. MachineScopes is the
// catalogue of the machine scopes, and a test pins these strings to it. User.Impersonation marks a user token; it is
// not a machine scope.
public static class Scopes
{
    public static class User
    {
        public const string Impersonation = UserScopes.Impersonation;
    }

    public static class Annotation
    {
        public const string Read = "Annotation.Read";
        public const string Write = "Annotation.Write";

        // Management scope (MachineScope.AnnotationManage): bulk create, soft delete and restore, on top of Annotation.Write.
        public const string Manage = "Annotation.Manage";
    }

    public static class Configuration
    {
        public const string Read = "Configuration.Read";
        public const string Write = "Configuration.Write";

        // Management scope (MachineScope.ConfigurationTransfer): the export and import of a view and of key/values.
        public const string Transfer = "Configuration.Transfer";
    }

    // Key/value secrets; they imply no Configuration scope.
    public static class Secrets
    {
        public const string Read = "Secrets.Read";
        public const string Write = "Secrets.Write";
    }

    // Management scopes, on top of Configuration.Write (MachineScope.SchemaWrite, TemplateWrite and KeyValueManage).
    public static class Schema
    {
        public const string Write = "Schema.Write";
    }

    public static class Template
    {
        public const string Write = "Template.Write";
    }

    public static class KeyValue
    {
        public const string Manage = "KeyValue.Manage";
    }
}
