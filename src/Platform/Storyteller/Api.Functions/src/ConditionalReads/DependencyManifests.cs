using System.Diagnostics.CodeAnalysis;

using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Binding;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace _42.Platform.Storyteller.Api.ConditionalReads;

// The dependency manifests of the resolved reads of this process, kept per caller and d tag, so a conditional read can
// revalidate the inputs of the tag it was sent instead of resolving again (ConfigurationHttp.GetConfigurationResolved).
// A manifest recorded by one caller never answers another's: every caller is resolved, metered and audited at least
// once per lifetime. An entry expires ManifestLifetime after the resolve that recorded it, never later, and the cache
// is bounded by ManifestCacheSizeLimit (one per entry plus one per input); a missing entry only costs a resolve.
public sealed class DependencyManifests : IDisposable
{
    private readonly MemoryCache _cache;
    private readonly TimeSpan _lifetime;
    private readonly IBindingCatalog? _catalog;

    public DependencyManifests(IOptions<ConditionalReadOptions> options, IBindingCatalog? catalog = null)
    {
        _lifetime = options.Value.ManifestLifetime;
        _catalog = catalog;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = Math.Max(1, options.Value.ManifestCacheSizeLimit) });
    }

    // The actor and the channel of a read: the audit trail of the binding reads (BindingRead) tells callers apart by them.
    public static string CallerOf(SecretRights rights)
    {
        var caller = rights.Caller;
        return $"{caller.Actor.Kind}:{caller.Actor.Id}:{caller.Channel}";
    }

    public string GetRuntimeVersion()
    {
        return BindingRuntimeVersion.Of(_catalog);
    }

    public bool TryGet(string caller, EntityTagHeaderValue tag, [MaybeNullWhen(false)] out DependencyManifest manifest)
    {
        return _cache.TryGetValue(KeyOf(caller, tag), out manifest);
    }

    public void Set(string caller, DependencyManifest manifest)
    {
        using var entry = _cache.CreateEntry(KeyOf(caller, manifest.Tag));
        entry.Value = manifest;
        entry.Size = 1 + manifest.Dependencies.Count;
        entry.AbsoluteExpirationRelativeToNow = _lifetime;
    }

    public void Remove(string caller, EntityTagHeaderValue tag)
    {
        _cache.Remove(KeyOf(caller, tag));
    }

    public void Dispose()
    {
        _cache.Dispose();
    }

    // The opaque tag: a tag sent without W/ finds the entry of its weak form.
    private static string KeyOf(string caller, EntityTagHeaderValue tag)
    {
        return $"{caller}\n{tag.Tag}";
    }
}
