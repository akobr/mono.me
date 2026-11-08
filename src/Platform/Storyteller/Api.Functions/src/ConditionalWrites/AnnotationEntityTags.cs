using System.Buffers.Text;
using System.Text;

using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace _42.Platform.Storyteller.Api.ConditionalWrites;

// Strong entity tags of an annotation's version, "a1.<base64url of the marker>": the marker is the storage version of
// the annotation item (the Cosmos DB _etag), so the tag changes with every replacement of the item and a no-op write
// keeps it. The prefix names the representation: a change of the annotation's JSON bumps it, so no tag of the old
// representation matches.
public static class AnnotationEntityTags
{
    public const string Prefix = "a1.";

    public static EntityTagHeaderValue Create(string marker)
    {
        ArgumentException.ThrowIfNullOrEmpty(marker);
        return new EntityTagHeaderValue($"\"{Prefix}{Base64Url.EncodeToString(Encoding.UTF8.GetBytes(marker))}\"", isWeak: false);
    }

    // The marker a tag names, or null for a tag of another kind: weak, of another prefix, or one that does not decode.
    public static string? GetMarker(EntityTagHeaderValue tag, bool allowWeak = false)
    {
        if ((tag.IsWeak && !allowWeak) || tag.Equals(EntityTagHeaderValue.Any))
        {
            return null;
        }

        var opaque = tag.Tag;

        if (opaque.Length < Prefix.Length + 2 || !opaque.AsSpan(1).StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var encoded = opaque.Subsegment(1 + Prefix.Length, opaque.Length - Prefix.Length - 2);

        try
        {
            return Encoding.UTF8.GetString(Base64Url.DecodeFromChars(encoded.AsSpan()));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // The value of the ETag header for a marker; null for none (no item, or a service that cannot tell).
    public static string? ToHeader(string? marker)
    {
        return string.IsNullOrEmpty(marker) || marker == Binding.BindingDependency.Absent
            ? null
            : Create(marker).ToString();
    }
}
