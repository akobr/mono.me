using System.Collections.Generic;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

// The bulk part of a set: annotations.jsonl. Lines and GeneratorLines as read (DataSetAnnotationLines), Annotations
// the annotations they stand for, Summary their outcomes (an annotation that could not be read is Failed), and what
// the bulk writer created besides them (in a dry run: would create).
public sealed record class DataSetLinesResult(
    int Lines,
    int GeneratorLines,
    int Annotations,
    ViewImportSummary Summary,
    IReadOnlyList<string> AncestorsCreated,
    int RelationUpdates);
