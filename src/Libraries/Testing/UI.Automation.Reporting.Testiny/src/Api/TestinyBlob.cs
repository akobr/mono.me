using System.Text.Json.Serialization;

namespace _42.Testing.UI.Automation.Reporting.Testiny.Api;

// An uploaded file (Blob), the answer of POST /testrun/upload-attachment/{trId}/{tcId}.
public sealed record TestinyBlob
{
    [JsonPropertyName("id")]
    public long? Id { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("filename")]
    public string? FileName { get; init; }

    [JsonPropertyName("mime_type")]
    public string? MimeType { get; init; }

    [JsonPropertyName("size")]
    public double? Size { get; init; }

    [JsonPropertyName("blob_id")]
    public string? BlobId { get; init; }
}
