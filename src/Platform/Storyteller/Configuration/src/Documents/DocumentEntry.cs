using _42.Platform.Storyteller.Sdk.Configuring.Reading;
using _42.Platform.Storyteller.Sdk.Configuring.References;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Configuring.Documents;

// One document in the memory cache of the document service: the server's document as it came, its content after
// substitution and its flattened keys. Never changed once built; a StorytellerDocument copies what a caller could
// change. The content may hold resolved secrets, so ToString() leaves it out.
internal sealed class DocumentEntry
{
    // Without the application's JsonConvert.DefaultSettings.
    private static readonly JsonSerializer Serializer = JsonSerializer.Create();

    public DocumentEntry(
        DocumentIdentity identity,
        string? etag,
        Configuration document,
        SubstitutionResult? substitution,
        IReadOnlyDictionary<string, string> data)
    {
        Identity = identity;
        ETag = etag;
        Document = document;
        Substitution = substitution;
        Data = data;
        Labels = Array.AsReadOnly(document.Labels?.ToArray() ?? []);
    }

    public DocumentIdentity Identity { get; }

    public string? ETag { get; }

    // The server's document, before local resolution.
    public Configuration Document { get; }

    // The content after substitution, with the time of its oldest resolution; null for a document without content.
    public SubstitutionResult? Substitution { get; }

    public IReadOnlyDictionary<string, string> Data { get; }

    public IReadOnlyList<string> Labels { get; }

    // The same document under the tag of a 304.
    public DocumentEntry WithETag(string? etag)
    {
        return new DocumentEntry(Identity, etag, Document, Substitution, Data);
    }

    public JObject CopyContent()
    {
        return Substitution?.Content.DeepClone() as JObject ?? new JObject();
    }

    public IConfigurationRoot BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(Data.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();
    }

    public IReadOnlyDictionary<string, object?> CopyValues()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var (name, value) in Document.Values ?? new Dictionary<string, object>())
        {
            values[name] = value is JToken token ? token.DeepClone() : value;
        }

        return values.AsReadOnly();
    }

    // Through JSON tokens, which keep the DateTimeOffset as it is.
    public AuditStamp? CopyModified()
    {
        return Document.Modified is { } modified
            ? JToken.FromObject(modified, Serializer).ToObject<AuditStamp>(Serializer)
            : null;
    }

    public override string ToString()
    {
        return $"{Identity} ({ETag ?? "no tag"})";
    }
}
