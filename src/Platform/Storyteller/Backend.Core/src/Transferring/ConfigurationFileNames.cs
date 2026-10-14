using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace _42.Platform.Storyteller.Transferring;

// File names of configuration documents in an archive: <key>.json for the current document and
// <key>.<version>.json for an earlier version. Each segment of the key percent-encodes the characters that
// file systems reject; names never contain '.', so the dots stay separators.
public static class ConfigurationFileNames
{
    public const string Extension = ".json";
    public const string ManifestFileName = "export.json";

    public static string GetFileName(string annotationKey, ulong? version = null)
    {
        var builder = new StringBuilder(annotationKey.Length + 16);
        var segments = annotationKey.Split(Constants.DefaultKeySeparatorChar);

        for (var index = 0; index < segments.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(Constants.DefaultKeySeparatorChar);
            }

            Encode(segments[index], builder);
        }

        if (version.HasValue)
        {
            builder.Append(Constants.DefaultKeySeparatorChar).Append(version.Value.ToString(CultureInfo.InvariantCulture));
        }

        return builder.Append(Extension).ToString();
    }

    // The key and, for a version file, the version of a file name (without folders).
    public static bool TryParse(string fileName, [NotNullWhen(true)] out AnnotationKey? key, out ulong? version)
    {
        key = null;
        version = null;

        if (!fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = fileName[..^Extension.Length].Split(Constants.DefaultKeySeparatorChar);

        if (segments.Length < 2
            || !AnnotationTypeCodes.ValidCodes.ContainsKey(segments[0])
            || segments.Any(segment => segment.Length == 0))
        {
            return false;
        }

        var expected = GetSegmentCount(AnnotationTypeCodes.ValidCodes[segments[0]]);

        if (segments.Length == expected + 1)
        {
            if (!ulong.TryParse(segments[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsedVersion))
            {
                return false;
            }

            version = parsedVersion;
            segments = segments[..^1];
        }
        else if (segments.Length != expected)
        {
            return false;
        }

        var decoded = new string[segments.Length];

        for (var index = 0; index < segments.Length; index++)
        {
            if (!TryDecode(segments[index], out var segment)
                || segment.Length == 0
                || segment.Contains(Constants.DefaultKeySeparatorChar))
            {
                version = null;
                return false;
            }

            decoded[index] = segment;
        }

        if (!AnnotationKey.TryParse(string.Join(Constants.DefaultKeySeparator, decoded), out key))
        {
            version = null;
            return false;
        }

        return true;
    }

    // The name of an export: configurations-{organization}-{project}-{view}[-git]-{yyyyMMdd-HHmmss}Z.zip, in UTC.
    public static string GetArchiveFileName(string organization, string project, string view, ConfigurationArchiveFormat format, DateTimeOffset createdAt)
    {
        var stamp = createdAt.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var kind = format == ConfigurationArchiveFormat.Git ? "-git" : string.Empty;
        return $"configurations-{organization}-{project}-{view}{kind}-{stamp}Z.zip";
    }

    private static int GetSegmentCount(AnnotationType type)
    {
        return type switch
        {
            AnnotationType.Responsibility or AnnotationType.Subject => 2,
            AnnotationType.Usage or AnnotationType.Context or AnnotationType.Unit => 3,
            AnnotationType.Execution => 4,
            AnnotationType.UnitOfExecution => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown annotation type."),
        };
    }

    private static void Encode(string segment, StringBuilder builder)
    {
        Span<byte> bytes = stackalloc byte[4];

        foreach (var rune in segment.EnumerateRunes())
        {
            if (!MustEncode(rune))
            {
                builder.Append(rune.ToString());
                continue;
            }

            var length = rune.EncodeToUtf8(bytes);

            for (var index = 0; index < length; index++)
            {
                builder.Append('%').Append(bytes[index].ToString("X2", CultureInfo.InvariantCulture));
            }
        }
    }

    private static bool MustEncode(Rune rune)
    {
        return rune.Value < 0x20
               || rune.Value == 0x7F
               || rune.Value is '%' or '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*';
    }

    private static bool TryDecode(string segment, [NotNullWhen(true)] out string? decoded)
    {
        if (!segment.Contains('%'))
        {
            decoded = segment;
            return true;
        }

        var bytes = new List<byte>(segment.Length);
        var text = new StringBuilder(segment.Length);

        for (var index = 0; index < segment.Length; index++)
        {
            if (segment[index] == '%')
            {
                if (index + 2 >= segment.Length
                    || !byte.TryParse(segment.AsSpan(index + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
                {
                    decoded = null;
                    return false;
                }

                bytes.Add(value);
                index += 2;
                continue;
            }

            FlushBytes();
            text.Append(segment[index]);
        }

        FlushBytes();
        decoded = text.ToString();
        return true;

        void FlushBytes()
        {
            if (bytes.Count == 0)
            {
                return;
            }

            text.Append(Encoding.UTF8.GetString(bytes.ToArray()));
            bytes.Clear();
        }
    }
}
