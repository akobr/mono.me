namespace _42.Platform.Storyteller.Transferring;

public static class ConfigurationTransferNames
{
    // The storage queue of the jobs and the blob container of their archives.
    public const string Queue = "configuration-transfers";
    public const string Container = "configuration-transfers";

    public static string GetArchiveName(ConfigurationTransferMessage message)
    {
        return GetArchiveName(message.Organization, message.Project, message.View, message.Kind, message.Id);
    }

    public static string GetArchiveName(string organization, string project, string view, ConfigurationTransferKind kind, string id)
    {
        var folder = kind == ConfigurationTransferKind.Export ? "exports" : "imports";
        return $"{organization}/{project}/{view}/{folder}/{id}.zip";
    }
}
