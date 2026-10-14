using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Transferring;

// One configuration file of an uploaded archive: its document, or why it cannot be imported.
public sealed record class ConfigurationArchiveFile
{
    public required string Path { get; init; }

    public required AnnotationKey Key { get; init; }

    public JObject? Content { get; init; }

    public string? ErrorCode { get; init; }

    public string? Message { get; init; }
}
