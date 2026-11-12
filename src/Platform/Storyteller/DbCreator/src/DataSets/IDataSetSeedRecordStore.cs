using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

public interface IDataSetSeedRecordStore
{
    // The record of the set in the target, or null when the set was never seeded there without failures.
    Task<DataSetSeedRecord?> GetAsync(DataSetTarget target, string set, CancellationToken cancellationToken = default);

    // Writes the record of the project of its partition, over an earlier one.
    Task SaveAsync(DataSetSeedRecord record, CancellationToken cancellationToken = default);
}
