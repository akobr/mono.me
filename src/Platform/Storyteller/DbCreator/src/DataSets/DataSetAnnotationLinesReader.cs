using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using _42.Platform.Storyteller.Transferring;
using Newtonsoft.Json.Linq;

using JsonException = Newtonsoft.Json.JsonException;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

// annotations.jsonl: one JSON object per line, the members of an annotation file plus its AnnotationKey, read as a
// file of an import reads (ViewAnnotationDocument), so numbers keep their text and dates stay strings. Blank lines and
// lines starting with // are skipped. A generator line has $count (and $start, 1 by default) and stands for $count
// annotations: {n} becomes the index and {0…0} the index padded to as many digits, in every string of the line, the
// key included. A line without $count is taken literally.
public static partial class DataSetAnnotationLinesReader
{
    public const string FileName = "annotations.jsonl";

    // Of one set, every line and every index of a generator counted.
    public const int MaxAnnotations = 200_000;

    public const string CountMember = "$count";
    public const string StartMember = "$start";

    // The annotations.jsonl of the set's folder, or null when it has none. The target gives the annotations their
    // project and view.
    public static DataSetAnnotationLines? Read(IFileSystem fileSystem, string folder, DataSetTarget target)
    {
        var path = fileSystem.Path.Combine(folder, FileName);

        if (!fileSystem.File.Exists(path))
        {
            return null;
        }

        using var reader = fileSystem.File.OpenText(path);
        return Read(reader, target, path);
    }

    // A line that cannot be read is an annotation that failed, with its line number. More than MaxAnnotations throws
    // DataSetException before any line is expanded; the source names the file in its message.
    public static DataSetAnnotationLines Read(TextReader reader, DataSetTarget target, string source = FileName)
    {
        var lines = new List<ParsedLine>();
        var total = 0L;
        var number = 0;

        while (reader.ReadLine() is { } text)
        {
            number++;
            var trimmed = text.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var line = Parse(number, trimmed);
            total += line.Failure is null ? line.Count : 1;

            if (total > MaxAnnotations)
            {
                throw new DataSetException(
                    $"'{source}' stands for more than {MaxAnnotations.ToString("N0", CultureInfo.InvariantCulture)} annotations at line {number}, the most a set holds.");
            }

            lines.Add(line);
        }

        var annotations = new List<DataSetAnnotationLine>((int)total);

        foreach (var line in lines)
        {
            if (line.Failure is not null)
            {
                annotations.Add(line.Failure);
            }
            else if (!line.IsGenerator)
            {
                annotations.Add(ReadAnnotation(line.Number, line.Document!, target));
            }
            else
            {
                for (var index = line.First; index < line.First + line.Count; index++)
                {
                    annotations.Add(ReadAnnotation(line.Number, Generate(line.Document!, index), target));
                }
            }
        }

        return new DataSetAnnotationLines(annotations, lines.Count, lines.Count(line => line.IsGenerator));
    }

    // The line's document for one index: {n} and {0…0} replaced in every string value, of any depth.
    public static JObject Generate(JObject document, long index)
    {
        var generated = (JObject)document.DeepClone();
        var text = index.ToString(CultureInfo.InvariantCulture);

        foreach (var value in generated.Descendants().OfType<JValue>().Where(value => value.Type == JTokenType.String).ToList())
        {
            value.Value = Placeholder().Replace(
                (string)value.Value!,
                match => match.Value == "{n}"
                    ? text
                    : index.ToString(new string('0', match.Length - 2), CultureInfo.InvariantCulture));
        }

        return generated;
    }

    // The document of a line without $count and $start, how many annotations it stands for, or why it stands for none.
    private static ParsedLine Parse(int number, string text)
    {
        JObject document;

        try
        {
            document = ViewAnnotationDocument.Parse(new MemoryStream(Encoding.UTF8.GetBytes(text), writable: false));
        }
        catch (JsonException exception)
        {
            return ParsedLine.Failed(number, false, $"The line is not a JSON object: {exception.Message}");
        }

        var count = TakeMember(document, CountMember);
        var start = TakeMember(document, StartMember);

        if (count is null)
        {
            return start is null
                ? new ParsedLine(number, document, false, 1, 1, null)
                : ParsedLine.Failed(number, false, $"The line has {StartMember} without {CountMember}.");
        }

        if (!TryGetWholeNumber(count, out var total) || total < 1)
        {
            return ParsedLine.Failed(number, true, $"{CountMember} must be a whole number of 1 or more, not {count}.");
        }

        var first = 1L;

        if (start is not null && !TryGetWholeNumber(start, out first))
        {
            return ParsedLine.Failed(number, true, $"{StartMember} must be a whole number of 0 or more, not {start}.");
        }

        return new ParsedLine(number, document, true, first, total, null);
    }

    private static DataSetAnnotationLine ReadAnnotation(int line, JObject document, DataSetTarget target)
    {
        var keyMember = document.Properties()
            .FirstOrDefault(property => property.Name.Equals(nameof(Annotation.AnnotationKey), StringComparison.OrdinalIgnoreCase));

        if (keyMember is null)
        {
            return Failed(line, null, ErrorCodes.InvalidDocument, $"The line has no {nameof(Annotation.AnnotationKey)}.");
        }

        if (keyMember.Value.Type != JTokenType.String || ParseKey(keyMember.Value.Value<string>()!) is not { } key)
        {
            return Failed(line, null, ErrorCodes.InvalidKey, $"{keyMember.Value.ToString(Newtonsoft.Json.Formatting.None)} is not an annotation key.");
        }

        // the key member stays: TryRead checks it against the key, as it does in a file
        var fullKey = FullKey.Create(key, target.Organization, target.Project, target.View);

        return ViewAnnotationDocument.TryRead(document, fullKey, out var annotation, out var errorCode, out var message)
            ? new DataSetAnnotationLine { Line = line, Key = key, Annotation = annotation }
            : Failed(line, key, errorCode, message);
    }

    // The type code in any case, as in a file name; the names as they are, without empty segments.
    private static AnnotationKey? ParseKey(string text)
    {
        var separator = text.IndexOf(Storyteller.Constants.DefaultKeySeparatorChar);

        if (separator <= 0)
        {
            return null;
        }

        var canonical = text[..separator].ToLowerInvariant() + text[separator..];

        return AnnotationKey.TryParse(canonical, out var key) && key.ToString() == canonical ? key : null;
    }

    private static JToken? TakeMember(JObject document, string name)
    {
        var property = document.Properties().FirstOrDefault(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        property?.Remove();
        return property?.Value;
    }

    // Numbers keep their text (JRaw), so 5, not 5.0, "5" or 5e0.
    private static bool TryGetWholeNumber(JToken token, out long value)
    {
        value = 0;
        var text = token is JValue { Type: JTokenType.Raw or JTokenType.Integer } number
            ? Convert.ToString(number.Value, CultureInfo.InvariantCulture)
            : null;

        return text is not null && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static DataSetAnnotationLine Failed(int line, AnnotationKey? key, string errorCode, string message)
    {
        return new DataSetAnnotationLine { Line = line, Key = key, ErrorCode = errorCode, Message = message };
    }

    [GeneratedRegex(@"\{(?:n|0+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    private sealed record class ParsedLine(int Number, JObject? Document, bool IsGenerator, long First, long Count, DataSetAnnotationLine? Failure)
    {
        public static ParsedLine Failed(int number, bool isGenerator, string message)
        {
            return new ParsedLine(number, null, isGenerator, 0, 0, DataSetAnnotationLinesReader.Failed(number, null, ErrorCodes.InvalidDocument, message));
        }
    }
}
