using _42.Platform.Storyteller.Sdk.Configuring.Documents;
using _42.Platform.Storyteller.Sdk.Configuring.Reading;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Configuring;

// A configuration read by IStorytellerDocuments. Every call returns a document of its own: what a caller changes in its
// Content, Values, Modified or Configuration changes neither the memory cache nor another caller's document. The
// content may hold resolved secrets, so ToString() leaves it out.
public sealed class StorytellerDocument
{
    private readonly DocumentEntry _entry;
    private readonly Lazy<JObject> _content;
    private readonly Lazy<IConfigurationRoot> _configuration;
    private readonly Lazy<IReadOnlyDictionary<string, object?>> _values;
    private readonly Lazy<AuditStamp?> _modified;

    internal StorytellerDocument(DocumentEntry entry)
    {
        _entry = entry;
        _content = new Lazy<JObject>(entry.CopyContent);
        _configuration = new Lazy<IConfigurationRoot>(entry.BuildConfiguration);
        _values = new Lazy<IReadOnlyDictionary<string, object?>>(entry.CopyValues);
        _modified = new Lazy<AuditStamp?>(entry.CopyModified);
    }

    // The full key, @{organization}.{project}.{view}.{annotation key}.
    public string Key => _entry.Identity.Key;

    public StorytellerReadMode Mode => _entry.Identity.Mode;

    // The tag of the platform's answer, which the next call sends back.
    public string? ETag => _entry.ETag;

    public long Version => _entry.Document.Version;

    public string? Hash => _entry.Document.Hash;

    public AuditStamp? Modified => _modified.Value;

    public IReadOnlyList<string> Labels => _entry.Labels;

    public IReadOnlyDictionary<string, object?> Values => _values.Value;

    // The document as the job should use it: in Resolved mode, its secret references are substituted.
    public JObject Content => _content.Value;

    // The flattened keys of Content (db:hosts:0), in a ConfigurationRoot over a memory source.
    public IConfiguration Configuration => _configuration.Value;

    // Binds the whole document, or a section of it, with ConfigurationBinder. Throws StorytellerConfigurationException
    // when there is nothing to bind, or when a value does not convert; the message names the type and the section, not
    // the value, which may be a secret.
    public T Get<T>(string? section = null)
    {
        var target = section is null ? Configuration : Configuration.GetSection(section);
        var at = section is null ? string.Empty : $" at {section}";

        if (!target.GetChildren().Any() && (target as IConfigurationSection)?.Value is null)
        {
            throw Failed($"The configuration {Key} has no values{at} to bind to {typeof(T).Name}.");
        }

        T? value;

        try
        {
            value = target.Get<T>();
        }
        catch (InvalidOperationException exception)
        {
            // The binder's message quotes the value that did not convert.
            throw Failed($"The configuration {Key} does not bind to {typeof(T).Name}{at}: the binder threw {exception.GetType().Name}.");
        }

        return value is null
            ? throw Failed($"The configuration {Key} has no values{at} to bind to {typeof(T).Name}.")
            : value;
    }

    public override string ToString()
    {
        return $"{Key} ({DocumentIdentity.ToText(Mode)}), version {Version}";
    }

    private StorytellerConfigurationException Failed(string message)
    {
        return new StorytellerConfigurationException(message, Key, null, null, false);
    }
}
