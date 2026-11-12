using System.Collections.Generic;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

// What a run seeds, decided from the files and the options alone: the sets in seed order (every set after its
// dependencies), the named sets among them, the target, the owner, and the descriptions of a created project and view.
public sealed record class DataSetSeedPlan(
    IReadOnlyList<DataSet> Sets,
    IReadOnlyList<DataSet> Named,
    DataSetTarget Target,
    DataSetOwner Owner,
    string? ProjectDescription,
    string? ViewDescription);
