using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using _42.Platform.Storyteller.Transferring;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

// What a set holds, read from its folder alone, for the listing of the sets: the documents per kind (those that
// cannot be read included), the files that are not documents, and the lines of annotations.jsonl with the annotations
// they stand for.
public sealed record class DataSetOverview
{
    public required DataSet Set { get; init; }

    // Every kind in import order, zero included.
    public required IReadOnlyList<(ViewArchiveContent Content, int Count)> Documents { get; init; }

    public int FailedDocuments { get; init; }

    public int IgnoredFiles { get; init; }

    // Null without annotations.jsonl.
    public DataSetAnnotationLines? Lines { get; init; }

    public int Annotations => Lines?.Annotations.Count ?? 0;

    public int FailedAnnotations => Lines?.Annotations.Count(annotation => annotation.Annotation is null) ?? 0;

    // DataSetException for a folder beyond the limits; a document or a line that cannot be read is counted.
    public static DataSetOverview Read(IFileSystem fileSystem, DataSet set)
    {
        var documents = DataSetFolder.ReadDocuments(fileSystem, set.Folder);
        var counts = documents.Files
            .GroupBy(file => ViewArchiveNames.GetContent(file.Kind))
            .ToDictionary(group => group.Key, group => group.Count());

        // the lines are read as for a seed; a set without a target of its own gets one that only names the annotations
        var manifest = set.Manifest;
        var target = new DataSetTarget(
            manifest.Organization ?? set.Name,
            manifest.Project ?? set.Name,
            manifest.View ?? Storyteller.Constants.DefaultViewName);
        var lines = DataSetAnnotationLinesReader.Read(fileSystem, set.Folder, target);

        return new DataSetOverview
        {
            Set = set,
            Documents = ViewArchiveInclude.All.Select(content => (content, counts.GetValueOrDefault(content))).ToList(),
            FailedDocuments = documents.Files.Count(file => file.ErrorCode is not null),
            IgnoredFiles = documents.IgnoredFiles,
            Lines = lines is null ? null : DataSetContent.WithoutDuplicates(documents, lines),
        };
    }
}
