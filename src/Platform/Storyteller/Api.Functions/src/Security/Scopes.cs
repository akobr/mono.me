namespace _42.Platform.Storyteller.Api.Security;

public static class Scopes
{
    public static class User
    {
        public const string Impersonation = "User.Impersonation";
    }

    public static class Default
    {
        public const string Read = "Default.Read";
        public const string Write = "Default.ReadWrite";
    }

    public static class Annotation
    {
        public const string Read = "Annotation.Read";
        public const string Write = "Annotation.ReadWrite";

        // Management scope (MachineManagementScope.AnnotationManage): machines only, never part of a preset.
        public const string Manage = "Annotation.Manage";
    }

    public static class Configuration
    {
        public const string Read = "Configuration.Read";
        public const string Write = "Configuration.ReadWrite";
        public const string Secrets = "Configuration.Secrets";

        // Management scope (MachineManagementScope.ConfigurationTransfer): machines only, never part of a preset.
        public const string Transfer = "Configuration.Transfer";
    }

    // Management scopes of machines only, never part of a preset (MachineManagementScope).
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
