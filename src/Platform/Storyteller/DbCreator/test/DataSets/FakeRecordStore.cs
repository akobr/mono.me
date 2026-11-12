using _42.Platform.Storyteller.DbCreator.DataSets;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

internal sealed class FakeRecordStore : IDataSetSeedRecordStore
{
    public Dictionary<string, DataSetSeedRecord> Records { get; } = new(StringComparer.Ordinal);

    public int Saves { get; private set; }

    public Task<DataSetSeedRecord?> GetAsync(DataSetTarget target, string set, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Records.GetValueOrDefault(Key(target.Organization, DataSetSeedRecord.GetPartitionKey(target.Project), DataSetSeedRecord.GetId(target.View, set))));
    }

    public Task SaveAsync(DataSetSeedRecord record, CancellationToken cancellationToken = default)
    {
        Saves++;
        Records[Key(record.Organization, record.PartitionKey, record.Id)] = record;
        return Task.CompletedTask;
    }

    private static string Key(string organization, string partitionKey, string id) => $"{organization}|{partitionKey}|{id}";
}
