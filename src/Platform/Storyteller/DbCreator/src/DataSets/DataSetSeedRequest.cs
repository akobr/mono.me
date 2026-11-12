using System.Collections.Generic;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

// One run of the seeder: the sets by name (without names, the sets of the folders), the folders added to the catalog,
// the target names that win over the manifests, and the owner (the base account without one).
public sealed record class DataSetSeedRequest
{
    public IReadOnlyList<string> Sets { get; init; } = [];

    public IReadOnlyList<string> Paths { get; init; } = [];

    public string? Organization { get; init; }

    public string? Project { get; init; }

    public string? View { get; init; }

    public string? Owner { get; init; }

    public DataSetSeedOptions Options { get; init; } = new();
}
