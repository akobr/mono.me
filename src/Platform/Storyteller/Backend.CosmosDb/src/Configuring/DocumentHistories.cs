using System.Collections.Generic;
using System.Linq;
using _42.Platform.Storyteller.Transferring;

namespace _42.Platform.Storyteller.Configuring;

// The documents an export reads, grouped by the document they belong to (a configuration's key, a template's type, a
// schema's identity): the current one and the versions still in the history.
internal sealed class DocumentHistories<TIdentity>
    where TIdentity : notnull
{
    private readonly Dictionary<TIdentity, (List<ConfigurationDocument> History, ConfigurationDocument? Current)> _documents;

    public DocumentHistories(IEqualityComparer<TIdentity>? comparer = null)
    {
        _documents = new Dictionary<TIdentity, (List<ConfigurationDocument> History, ConfigurationDocument? Current)>(comparer);
    }

    // An empty document is not a version: an empty configuration only caches the effective one, and an empty template
    // or schema has no effect.
    public void Add(TIdentity identity, DocumentArchiveRow row, bool isCurrent)
    {
        if (row.Content is not { HasValues: true })
        {
            return;
        }

        var document = new ConfigurationDocument
        {
            Version = row.Version,
            Author = row.Modified.By.Name ?? row.Modified.By.Id,
            Actor = row.Modified.By,
            CreatedAt = row.Modified.At,
            Content = row.Content,
        };

        if (!_documents.TryGetValue(identity, out var entry))
        {
            entry = ([], null);
        }

        if (isCurrent)
        {
            entry.Current = document;
        }
        else
        {
            entry.History.Add(document);
        }

        _documents[identity] = entry;
    }

    // Per document its versions, oldest first with the current one last, and the current one, null for a deleted
    // document. A history item that is not older than the current document is left out.
    public IEnumerable<(TIdentity Identity, IReadOnlyList<ConfigurationDocument> Versions, ConfigurationDocument? Current)> Build()
    {
        foreach (var (identity, (history, current)) in _documents)
        {
            var versions = history
                .Where(version => current is null || version.Version < current.Version)
                .OrderBy(version => version.Version)
                .ToList();

            if (current is not null)
            {
                versions.Add(current);
            }

            yield return (identity, versions, current);
        }
    }
}
