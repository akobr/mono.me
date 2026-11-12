using System.Collections.Generic;
using System.Linq;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

// One run of the seeder: its target and owner, what it created for them, and the sets in the order they were seeded.
public sealed record class DataSetRunResult(
    DataSetTarget Target,
    DataSetOwner Owner,
    DataSetProvisioning Provisioning,
    IReadOnlyList<DataSetSeedResult> Sets,
    bool IsDryRun)
{
    public bool HasFailures => Sets.Any(set => set.HasFailures);
}
