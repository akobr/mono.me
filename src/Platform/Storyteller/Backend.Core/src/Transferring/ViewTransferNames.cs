namespace _42.Platform.Storyteller.Transferring;

public static class ViewTransferNames
{
    // The storage queue of the jobs and the blob container of their archives. They keep their first names: jobs
    // live a day, and renaming deployed resources gains nothing.
    public const string Queue = "configuration-transfers";
    public const string Container = "configuration-transfers";

    // Where the Functions runtime moves a message after its last failed delivery (maxDequeueCount of host.json).
    public const string PoisonQueue = Queue + "-poison";

    public static string GetArchiveName(ViewTransferMessage message)
    {
        return GetArchiveName(message.Organization, message.Project, message.View, message.Kind, message.Id);
    }

    public static string GetArchiveName(string organization, string project, string view, ViewTransferKind kind, string id)
    {
        var folder = kind == ViewTransferKind.Export ? "exports" : "imports";
        return $"{organization}/{project}/{view}/{folder}/{id}.zip";
    }
}
