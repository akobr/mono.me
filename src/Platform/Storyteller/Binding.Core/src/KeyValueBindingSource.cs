using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using _42.Platform.Storyteller.Binding.Language;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.KeyValues;

namespace _42.Platform.Storyteller.Binding;

public enum KeyValueSourceKind
{
    // Public pairs and secrets; a secret only with secrets included.
    Default = 0,

    // Public pairs only.
    Public,

    // Secrets only, with secrets included.
    Secret,
}

public static class KeyValueBindingSources
{
    public const string DefaultKey = BindingExecutor.DefaultSourceKey;
    public const string PublicKey = "public";
    public const string SecretKey = "secret";

    public static readonly IReadOnlyDictionary<string, KeyValueSourceKind> Kinds = new Dictionary<string, KeyValueSourceKind>(StringComparer.Ordinal)
    {
        [DefaultKey] = KeyValueSourceKind.Default,
        [PublicKey] = KeyValueSourceKind.Public,
        [SecretKey] = KeyValueSourceKind.Secret,
    };

    // Source names the native store takes over when AddKeyValueBindings is registered after the other sources.
    public static readonly IReadOnlySet<string> ReservedKeys = Kinds.Keys.ToHashSet(StringComparer.Ordinal);
}

/// <summary>
/// The native key/value store as a binding source: <c>@db.host</c> (default), <c>@public(db.host)</c> and
/// <c>@secret(db.password)</c>. The pairs come from the view of the configuration being resolved, so the
/// source declines outside a configuration read. Each key is looked up at most once per resolved read.
/// </summary>
public sealed class KeyValueBindingSource : IBindingSource
{
    // shared by the three kinds: one lookup per key and read, released with the read's context;
    // Lazy, because the properties of a document are resolved in parallel and GetOrAdd alone may run a factory twice
    private static readonly ConditionalWeakTable<ConfigurationBindingContext, ConcurrentDictionary<string, Lazy<Task<KeyValueLookup?>>>> Memo = new();

    private readonly Lazy<IKeyValueService> _service;
    private readonly KeyValueSourceKind _kind;

    public KeyValueBindingSource(Lazy<IKeyValueService> service, KeyValueSourceKind kind)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _kind = kind;
    }

    public async ValueTask<BindingValue?> ResolveAsync(BindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context is not ConfigurationBindingContext context)
        {
            return null;
        }

        var includeSecrets = _kind != KeyValueSourceKind.Public && request.IncludeSecrets;
        var lookup = await LookupAsync(context, request.PathString, includeSecrets);

        if (lookup is null)
        {
            return null;
        }

        if (_kind == KeyValueSourceKind.Public && lookup.Protection.IsSecret())
        {
            return null;
        }

        if (_kind == KeyValueSourceKind.Secret && !lookup.Protection.IsSecret())
        {
            return null;
        }

        return BindingValue.FromString(lookup.Value);
    }

    private Task<KeyValueLookup?> LookupAsync(ConfigurationBindingContext context, string key, bool includeSecrets)
    {
        var memo = Memo.GetValue(context, _ => new ConcurrentDictionary<string, Lazy<Task<KeyValueLookup?>>>(StringComparer.Ordinal));
        var memoKey = $"{(includeSecrets ? "s" : "p")}:{key.ToLowerInvariant()}";
        var configurationKey = context.ConfigurationKey;

        return memo.GetOrAdd(memoKey, _ => new Lazy<Task<KeyValueLookup?>>(() => _service.Value.LookupAsync(
            configurationKey.OrganizationName,
            configurationKey.ProjectName,
            configurationKey.ViewName,
            key,
            includeSecrets))).Value;
    }
}
