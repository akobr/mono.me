namespace _42.Testing.UI.Automation.Runtime.Artifacts;

internal static class MimeTypes
{
    public static string Of(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".zip" => "application/zip",
            ".webm" => "video/webm",
            ".log" or ".txt" => "text/plain",
            ".json" or ".jsonl" => "application/json",
            ".html" or ".htm" => "text/html",
            ".har" => "application/json",
            _ => "application/octet-stream",
        };
    }
}
