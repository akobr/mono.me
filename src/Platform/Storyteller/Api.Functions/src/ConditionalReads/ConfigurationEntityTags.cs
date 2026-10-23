using System.Buffers.Binary;
using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Binding;

using Microsoft.Net.Http.Headers;

using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Api.ConditionalReads;

// Weak entity tags of the configuration reads, W/"<prefix>.<digest>": the first 16 bytes of SHA-256 over the fields of the body,
// base64url. Every input is length-prefixed, so different inputs never hash alike. The full key ties a tag to its configuration.
//   e  the effective read: full key, Version, Hash and Content.
//   r  a resolved read of a document without bindings, which is the effective document: the same plus the secret rights.
//   b  a resolved read with bindings whose inputs are volatile: the same over the resolved Content, keyed (HMAC-SHA256),
//      because it can carry secrets.
//   d  a resolved read with bindings whose inputs were all recorded: the r tag of the effective read, the runtime version
//      and the inputs with their storage markers (the dependency manifest), so it names the resolved body without hashing
//      it. Not keyed: none of it is secret.
//   c  the composition read: full key and the whole composition (every source with its version, presence and content, the
//      nodes, steps and origins), so a change of any source changes the tag, even one that leaves Content as it was.
// Modified is left out of e, r, b and d: past version 0 it changes only with Version, and a configuration without an item
// stamps every read. c keeps the stamps of the sources: a stored document's changes only with its version, and a Missing
// source has none.
public static class ConfigurationEntityTags
{
    public const string EffectivePrefix = "e";

    public const string ResolvedPrefix = "r";

    public const string BoundPrefix = "b";

    public const string DependenciesPrefix = "d";

    public const string CompositionPrefix = "c";

    private const int DigestBytes = 16;

    public static EntityTagHeaderValue ForEffective(FullKey key, Configuration configuration)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        return Create(hash, EffectivePrefix, key, rights: null, configuration);
    }

    public static EntityTagHeaderValue ForResolved(FullKey key, Configuration configuration, SecretRights rights)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        return Create(hash, ResolvedPrefix, key, rights, configuration);
    }

    public static EntityTagHeaderValue ForResolvedBindings(FullKey key, Configuration resolved, SecretRights rights, byte[] secret)
    {
        using var hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, secret);
        return Create(hash, BoundPrefix, key, rights, resolved);
    }

    // effective is the r tag of the read, taken before the resolve; dependencies come sorted from BindingDependencies.ToList.
    public static EntityTagHeaderValue ForDependencies(EntityTagHeaderValue effective, string runtime, IReadOnlyList<BindingDependency> dependencies)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, DependenciesPrefix);
        Append(hash, effective.Tag.ToString());
        Append(hash, runtime);
        Append(hash, dependencies.Count.ToString(CultureInfo.InvariantCulture));

        foreach (var dependency in dependencies)
        {
            Append(hash, dependency.Kind);
            Append(hash, dependency.Source);
            Append(hash, dependency.Key);
            Append(hash, dependency.Marker);
        }

        return Finish(hash, DependenciesPrefix);
    }

    // Whether the tag names a dependency manifest; it says nothing about whose.
    public static bool IsDependencies(EntityTagHeaderValue tag)
    {
        return tag.Tag.StartsWith($"\"{DependenciesPrefix}.", StringComparison.Ordinal);
    }

    public static EntityTagHeaderValue ForComposition(FullKey key, ConfigurationComposition composition)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, CompositionPrefix);
        Append(hash, key.ToString());
        Append(hash, JsonConvert.SerializeObject(composition, Formatting.None));
        return Finish(hash, CompositionPrefix);
    }

    // s: the caller may read secrets; w: the read right is withheld from a machine without mTLS; p: neither.
    // w and p get the same body, but only w gets the withheld header, so they don't share a tag either.
    private static string RightsCode(SecretRights rights)
    {
        return rights.CanReadSecrets ? "s" : rights.IsReadWithheldByMtls ? "w" : "p";
    }

    private static EntityTagHeaderValue Create(IncrementalHash hash, string prefix, FullKey key, SecretRights? rights, Configuration configuration)
    {
        Append(hash, prefix);
        Append(hash, key.ToString());
        Append(hash, rights is null ? null : RightsCode(rights));
        Append(hash, configuration.Version.ToString(CultureInfo.InvariantCulture));
        Append(hash, configuration.Hash);
        Append(hash, configuration.Content.ToString(Formatting.None));
        return Finish(hash, prefix);
    }

    private static EntityTagHeaderValue Finish(IncrementalHash hash, string prefix)
    {
        Span<byte> digest = stackalloc byte[hash.HashLengthInBytes];
        hash.GetHashAndReset(digest);
        return new EntityTagHeaderValue($"\"{prefix}.{Base64Url.EncodeToString(digest[..DigestBytes])}\"", isWeak: true);
    }

    // A length prefix, -1 for null, then the UTF-8 bytes.
    private static void Append(IncrementalHash hash, string? value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, value is null ? -1 : Encoding.UTF8.GetByteCount(value));
        hash.AppendData(length);

        if (value is not null)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(value));
        }
    }
}
