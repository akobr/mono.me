using System.Collections.Generic;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

// What annotations.jsonl holds: its annotations in line order (each index of a generator line in turn), the lines that
// hold an annotation or a generator (blank lines and comments do not count), and the generator lines among them.
public sealed record class DataSetAnnotationLines(IReadOnlyList<DataSetAnnotationLine> Annotations, int Lines, int GeneratorLines);
