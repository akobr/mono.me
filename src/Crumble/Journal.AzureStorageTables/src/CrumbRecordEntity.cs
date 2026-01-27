using System;
using System.Globalization;
using Azure.Data.Tables;

namespace _42.Crumble;

public enum CrumbState
{
    Enqueued = 0,
    Executing,
    Completed,
    Failed,
}

public record CrumbRecordEntity(
    DateOnly Date,
    Guid CrumbId,
    string CrumbKey,
    string? ActionType,
    Guid? ActionId,
    CrumbState State,
    TimeOnly ExecutionStartedUtc,
    TimeSpan ExecutionDuration)
{
    public static CrumbRecordEntity FromTableEntity(TableEntity e) => new(
        DateOnly.ParseExact(e.PartitionKey, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        Guid.ParseExact(e.RowKey, "D"),
        e.GetString("CrumbKey")!,
        e.GetString("ActionType"),
        e.GetGuid("ActionId"),
        (CrumbState)e.GetInt32("State"),
        TimeOnly.ParseExact(e.GetString("ExecutionStartedUtc"), "O", CultureInfo.InvariantCulture),
        TimeSpan.ParseExact(e.GetString("ExecutionDuration"), "G", CultureInfo.InvariantCulture)
    );

    public TableEntity ToTableEntity() => new(
        Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        CrumbId.ToString("D"))
    {
        ["CrumbKey"] = CrumbKey,
        ["ActionType"] = ActionType,
        ["ActionId"] = ActionId,
        ["State"] = State,
        ["ExecutionStartedUtc"] = ExecutionStartedUtc.ToString("O", CultureInfo.InvariantCulture),
        ["ExecutionDuration"] = ExecutionDuration.ToString("G", CultureInfo.InvariantCulture),
    };
}
