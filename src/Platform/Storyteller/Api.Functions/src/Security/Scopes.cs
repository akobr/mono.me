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
    }

    public static class Configuration
    {
        public const string Read = "Configuration.Read";
        public const string Write = "Configuration.ReadWrite";
    }
}
