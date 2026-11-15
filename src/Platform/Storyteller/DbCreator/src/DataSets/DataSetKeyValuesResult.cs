using System.Collections.Generic;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

// The pairs of a set: key-values.json. Pairs and Levels as read (DataSetKeyValues), Summary their outcomes (in a dry
// run: what a seed would do).
public sealed record class DataSetKeyValuesResult(
    int Pairs,
    IReadOnlyList<(KeyValueProtection Level, int Count)> Levels,
    ViewImportSummary Summary);
