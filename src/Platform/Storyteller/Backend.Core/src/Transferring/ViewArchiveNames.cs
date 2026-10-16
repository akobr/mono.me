using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace _42.Platform.Storyteller.Transferring;

// File names of the documents in an archive. The name tells the kind, so folders play no part:
//
//   <key>.json                       annotation (annotations have no versions)
//   <key>.conf[.<n>].json            configuration
//   <type>.template[.<n>].json       template
//   <type>.schema[.<n>].json         type schema
//   <key>.schema[.<n>].json          annotation schema
//   <key>.<type>.schema[.<n>].json   descendant-type schema below the key
//
// <n> is an earlier version. Each segment of a key percent-encodes the characters that file systems reject; names
// never contain '.', so the dots stay separators. A key whose second segment is "template" or "schema" (in any
// case) gets the first character of that segment encoded, so it never reads as a file of its type.
public static class ViewArchiveNames
{
    public const string Extension = ".json";
    public const string ManifestFileName = "export.json";

    public const string ConfigurationSuffix = "conf";
    public const string TemplateSuffix = "template";
    public const string SchemaSuffix = "schema";

    // The pairs (ancestor type, descendant type) of AnnotationHierarchy, read from one sample key of every type.
    private static readonly HashSet<(string Ancestor, string Descendant)> DescendantTypes = AnnotationTypeCodes.ValidCodes
        .SelectMany(code => AnnotationHierarchy.GetAncestorSchemaSources(AnnotationKey.Parse(
            string.Join(Constants.DefaultKeySeparator, Enumerable.Repeat("x", GetSegmentCount(code.Value) - 1).Prepend(code.Key)))))
        .Select(source => (source.Ancestor.TypeCode, source.DescendantTypeCode))
        .ToHashSet();

    public static string GetAnnotationFileName(string annotationKey)
    {
        return BuildKeyName(annotationKey).Append(Extension).ToString();
    }

    public static string GetConfigurationFileName(string annotationKey, ulong? version = null)
    {
        var builder = BuildKeyName(annotationKey).Append(Constants.DefaultKeySeparatorChar).Append(ConfigurationSuffix);
        return AppendVersion(builder, version).Append(Extension).ToString();
    }

    public static string GetTemplateFileName(string annotationType, ulong? version = null)
    {
        var builder = new StringBuilder(NormalizeType(annotationType)).Append(Constants.DefaultKeySeparatorChar).Append(TemplateSuffix);
        return AppendVersion(builder, version).Append(Extension).ToString();
    }

    public static string GetTypeSchemaFileName(string annotationType, ulong? version = null)
    {
        var builder = new StringBuilder(NormalizeType(annotationType)).Append(Constants.DefaultKeySeparatorChar).Append(SchemaSuffix);
        return AppendVersion(builder, version).Append(Extension).ToString();
    }

    public static string GetAnnotationSchemaFileName(string annotationKey, ulong? version = null)
    {
        var builder = BuildKeyName(annotationKey).Append(Constants.DefaultKeySeparatorChar).Append(SchemaSuffix);
        return AppendVersion(builder, version).Append(Extension).ToString();
    }

    public static string GetDescendantTypeSchemaFileName(string ancestorKey, string annotationType, ulong? version = null)
    {
        var builder = BuildKeyName(ancestorKey)
            .Append(Constants.DefaultKeySeparatorChar).Append(NormalizeType(annotationType))
            .Append(Constants.DefaultKeySeparatorChar).Append(SchemaSuffix);
        return AppendVersion(builder, version).Append(Extension).ToString();
    }

    // The file name of a document of any kind; annotations have no versions.
    public static string GetFileName(ViewDocumentKind kind, string? annotationType, string? annotationKey, ulong? version = null)
    {
        var key = kind is ViewDocumentKind.Template or ViewDocumentKind.TypeSchema
            ? null
            : annotationKey ?? throw new ArgumentNullException(nameof(annotationKey));
        var type = kind is ViewDocumentKind.Template or ViewDocumentKind.TypeSchema or ViewDocumentKind.DescendantTypeSchema
            ? annotationType ?? throw new ArgumentNullException(nameof(annotationType))
            : null;

        return kind switch
        {
            ViewDocumentKind.Annotation when version is null => GetAnnotationFileName(key!),
            ViewDocumentKind.Configuration => GetConfigurationFileName(key!, version),
            ViewDocumentKind.Template => GetTemplateFileName(type!, version),
            ViewDocumentKind.TypeSchema => GetTypeSchemaFileName(type!, version),
            ViewDocumentKind.AnnotationSchema => GetAnnotationSchemaFileName(key!, version),
            ViewDocumentKind.DescendantTypeSchema => GetDescendantTypeSchemaFileName(key!, type!, version),
            ViewDocumentKind.Annotation => throw new ArgumentOutOfRangeException(nameof(version), version, "Annotations have no versions."),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown document kind."),
        };
    }

    // The document a file name (without folders) stands for, or false when it is not a document.
    public static bool TryParse(string fileName, [NotNullWhen(true)] out ViewArchiveName? name)
    {
        name = null;

        if (!fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = fileName[..^Extension.Length].Split(Constants.DefaultKeySeparatorChar);

        if (segments.Length < 2
            || segments.Any(segment => segment.Length == 0)
            || !AnnotationTypeCodes.ValidCodes.TryGetValue(segments[0], out var type))
        {
            return false;
        }

        var typeCode = segments[0].ToLowerInvariant();

        // a literal (not encoded) second segment "template" or "schema" is a file of the type itself
        if (IsWord(segments[1], TemplateSuffix) || IsWord(segments[1], SchemaSuffix))
        {
            var kind = IsWord(segments[1], TemplateSuffix) ? ViewDocumentKind.Template : ViewDocumentKind.TypeSchema;
            ulong? typeVersion = null;

            if (segments.Length > 3 || (segments.Length == 3 && !TryParseVersion(segments[2], out typeVersion)))
            {
                return false;
            }

            name = new ViewArchiveName { Kind = kind, AnnotationType = typeCode, Version = typeVersion };
            return true;
        }

        var count = GetSegmentCount(type);

        if (segments.Length < count || !TryDecodeKey(typeCode, segments.AsSpan(1, count - 1), out var key))
        {
            return false;
        }

        name = segments[count..] switch
        {
            [] => new ViewArchiveName { Kind = ViewDocumentKind.Annotation, AnnotationKey = key },
            [var suffix] when IsWord(suffix, ConfigurationSuffix) => new ViewArchiveName { Kind = ViewDocumentKind.Configuration, AnnotationKey = key },
            [var suffix, var version] when IsWord(suffix, ConfigurationSuffix) && TryParseVersion(version, out var parsed) =>
                new ViewArchiveName { Kind = ViewDocumentKind.Configuration, AnnotationKey = key, Version = parsed },
            [var suffix] when IsWord(suffix, SchemaSuffix) => new ViewArchiveName { Kind = ViewDocumentKind.AnnotationSchema, AnnotationKey = key },
            [var suffix, var version] when IsWord(suffix, SchemaSuffix) && TryParseVersion(version, out var parsed) =>
                new ViewArchiveName { Kind = ViewDocumentKind.AnnotationSchema, AnnotationKey = key, Version = parsed },
            [var descendant, var suffix] when IsWord(suffix, SchemaSuffix) && AnnotationTypeCodes.ValidCodes.ContainsKey(descendant) =>
                Descendant(key, descendant.ToLowerInvariant(), null),
            [var descendant, var suffix, var version] when IsWord(suffix, SchemaSuffix) && AnnotationTypeCodes.ValidCodes.ContainsKey(descendant) && TryParseVersion(version, out var parsed) =>
                Descendant(key, descendant.ToLowerInvariant(), parsed),
            _ => null,
        };

        return name is not null;
    }

    // A path (with '/' separators) that an archive's reader passes over without counting it: any hidden folder or
    // file (a segment starting with '.': .git, .github, .vscode, .DS_Store, ._*) and the __MACOSX folder of a zip.
    // A "./" prefix is the current folder, not a hidden one.
    public static bool IsSkippedPath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return segments.Length == 0
               || segments.Any(segment => segment.StartsWith('.') && segment != ".")
               || segments[0].Equals("__MACOSX", StringComparison.OrdinalIgnoreCase);
    }

    // The name of an export: export-{organization}-{project}-{view}[-git]-{yyyyMMdd-HHmmss}Z.zip, in UTC.
    public static string GetArchiveFileName(string organization, string project, string view, ViewArchiveFormat format, DateTimeOffset createdAt)
    {
        var stamp = createdAt.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var kind = format == ViewArchiveFormat.Git ? "-git" : string.Empty;
        return $"export-{organization}-{project}-{view}{kind}-{stamp}Z.zip";
    }

    public static ViewArchiveContent GetContent(ViewDocumentKind kind)
    {
        return kind switch
        {
            ViewDocumentKind.Annotation => ViewArchiveContent.Annotations,
            ViewDocumentKind.TypeSchema or ViewDocumentKind.DescendantTypeSchema or ViewDocumentKind.AnnotationSchema => ViewArchiveContent.Schemas,
            ViewDocumentKind.Template => ViewArchiveContent.Templates,
            ViewDocumentKind.Configuration => ViewArchiveContent.Configurations,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown document kind."),
        };
    }

    // Whether annotations of the descendant type can descend from the ancestor key (AnnotationHierarchy).
    public static bool CanDescend(AnnotationKey ancestorKey, string descendantType)
    {
        return DescendantTypes.Contains((ancestorKey.TypeCode.ToLowerInvariant(), descendantType.ToLowerInvariant()));
    }

    private static ViewArchiveName Descendant(AnnotationKey key, string descendantType, ulong? version)
    {
        var name = new ViewArchiveName
        {
            Kind = ViewDocumentKind.DescendantTypeSchema,
            AnnotationType = descendantType,
            AnnotationKey = key,
            Version = version,
        };

        // still a document, so a misplaced schema is reported instead of silently ignored
        return CanDescend(key, descendantType)
            ? name
            : name with
            {
                ErrorCode = ErrorCodes.InvalidKey,
                Message = $"The type {descendantType} cannot descend from {key}, so it has no descendant-type schema there.",
            };
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

    private static string NormalizeType(string annotationType)
    {
        var normalized = annotationType.ToLowerInvariant();

        return AnnotationTypeCodes.ValidCodes.ContainsKey(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(annotationType), annotationType, "Unknown annotation type code.");
    }

    private static bool IsWord(string segment, string word)
    {
        return segment.Equals(word, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseVersion(string segment, out ulong? version)
    {
        if (ulong.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            version = parsed;
            return true;
        }

        version = null;
        return false;
    }

    private static StringBuilder BuildKeyName(string annotationKey)
    {
        var builder = new StringBuilder(annotationKey.Length + 24);
        var segments = annotationKey.Split(Constants.DefaultKeySeparatorChar);

        for (var index = 0; index < segments.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(Constants.DefaultKeySeparatorChar);
            }

            var start = builder.Length;
            Encode(segments[index], builder);

            // the reserved second segment: its first character (an ASCII letter) is encoded
            if (index == 1 && (IsWord(segments[index], TemplateSuffix) || IsWord(segments[index], SchemaSuffix)))
            {
                var first = builder[start];
                builder.Remove(start, 1).Insert(start, $"%{(int)first:X2}");
            }
        }

        return builder;
    }

    private static StringBuilder AppendVersion(StringBuilder builder, ulong? version)
    {
        return version.HasValue
            ? builder.Append(Constants.DefaultKeySeparatorChar).Append(version.Value.ToString(CultureInfo.InvariantCulture))
            : builder;
    }

    private static bool TryDecodeKey(string typeCode, ReadOnlySpan<string> segments, [NotNullWhen(true)] out AnnotationKey? key)
    {
        key = null;
        var decoded = new string[segments.Length + 1];
        decoded[0] = typeCode;

        for (var index = 0; index < segments.Length; index++)
        {
            if (!TryDecode(segments[index], out var segment)
                || segment.Length == 0
                || segment.Contains(Constants.DefaultKeySeparatorChar))
            {
                return false;
            }

            decoded[index + 1] = segment;
        }

        return AnnotationKey.TryParse(string.Join(Constants.DefaultKeySeparator, decoded), out key);
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
