using System;
using System.Collections.Generic;
using System.Linq;
using _42.Platform.Storyteller.Transferring;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

// The seed of one set. Already seeded: its record has its version and hash, so nothing was read further. Not seeded:
// a set it depends on failed (FailedDependency). Otherwise Documents counts the document files per kind, Lines the
// annotations of annotations.jsonl, Items lists every outcome but Created and Unchanged, and IsRecorded tells whether
// the record was written (never in a dry run or after a failure).
public sealed record class DataSetSeedResult
{
    public required DataSet Set { get; init; }

    public string? Hash { get; init; }

    public bool IsAlreadySeeded { get; init; }

    public string? FailedDependency { get; init; }

    public ViewImportTally? Documents { get; init; }

    public DataSetLinesResult? Lines { get; init; }

    public IReadOnlyList<DataSetSeedItem> Items { get; init; } = [];

    public bool IsRecorded { get; init; }

    public TimeSpan Elapsed { get; init; }

    public bool IsSeeded => !IsAlreadySeeded && FailedDependency is null;

    public bool HasFailures => FailedDependency is not null || Items.Any(item => item.Outcome == ViewImportOutcome.Failed);

    public IEnumerable<DataSetSeedItem> Failures => Items.Where(item => item.Outcome == ViewImportOutcome.Failed);
}
