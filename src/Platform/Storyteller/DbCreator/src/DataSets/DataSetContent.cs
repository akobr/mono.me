using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using _42.Platform.Storyteller.Transferring;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

// What a set seeds: its document files and its annotations.jsonl (null without one).
public sealed record class DataSetContent(ViewArchiveDocuments Documents, DataSetAnnotationLines? Lines)
{
    // Reads the set's folder for the target. One key is one document, as in an archive: an annotation of
    // annotations.jsonl whose key a file already holds, or an earlier line, fails with DuplicateFile.
    public static DataSetContent Read(IFileSystem fileSystem, string folder, DataSetTarget target)
    {
        var documents = DataSetFolder.ReadDocuments(fileSystem, folder);
        var lines = DataSetAnnotationLinesReader.Read(fileSystem, folder, target);

        return new DataSetContent(documents, lines is null ? null : WithoutDuplicates(documents, lines));
    }

    public static DataSetAnnotationLines WithoutDuplicates(ViewArchiveDocuments documents, DataSetAnnotationLines lines)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in documents.Files.Where(file => file.Kind == ViewDocumentKind.Annotation && file.AnnotationKey is not null))
        {
            files.TryAdd(file.AnnotationKey!.ToString(), file.Path);
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var annotations = new List<DataSetAnnotationLine>(lines.Annotations.Count);

        foreach (var annotation in lines.Annotations)
        {
            if (annotation.Key is null)
            {
                annotations.Add(annotation);
                continue;
            }

            var key = annotation.Key.ToString();

            if (files.TryGetValue(key, out var path))
            {
                annotations.Add(Duplicate(annotation, $"The file '{path}' already holds the annotation {key}."));
            }
            else if (!seen.TryAdd(key, annotation.Line))
            {
                annotations.Add(Duplicate(annotation, $"Line {seen[key]} of {DataSetAnnotationLinesReader.FileName} already holds the annotation {key}."));
            }
            else
            {
                annotations.Add(annotation);
            }
        }

        return lines with { Annotations = annotations };
    }

    private static DataSetAnnotationLine Duplicate(DataSetAnnotationLine annotation, string message)
    {
        return annotation with { Annotation = null, ErrorCode = ErrorCodes.DuplicateFile, Message = message };
    }
}
