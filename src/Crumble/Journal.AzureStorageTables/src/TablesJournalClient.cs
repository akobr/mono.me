using System.Globalization;
using Azure.Data.Tables;

namespace _42.Crumble;

public class TablesJournalClient(TableServiceClient service) : IJournalClient
{
    private readonly TableClient _client = service.GetTableClient("Crumbs");

    public async Task EnrollBeginningOfCrumb(ICrumbInnerExecutionContext context)
    {
        var actionType = context.ExecutionContext.Properties.TryGetValue("#Crumble.Exec.ActionType", out var rawActionType)
            ? rawActionType.ToString()
            : null;

        var actionId = context.ExecutionContext.Properties.TryGetValue("#Crumble.Exec.ActionId", out var rawActionId)
            ? Guid.TryParseExact(rawActionId.ToString(), "D", out var actionIdGuid)
                ? new Guid?(actionIdGuid)
                : null
            : null;

        var utcStartTime = context.StartTime.UtcDateTime;

        var record = new CrumbRecordEntity(
            DateOnly.FromDateTime(utcStartTime),
            context.ExecutionContext.Id,
            context.CrumbKey,
            actionType,
            actionId,
            CrumbState.Executing,
            TimeOnly.FromDateTime(utcStartTime),
            TimeSpan.Zero);

        using var response = await _client.AddEntityAsync(record.ToTableEntity());

        if (response.IsError)
        {
            throw new InvalidOperationException(
                $"Unable add journal table entity, with status {response.Status} and reason '{response.ReasonPhrase}'.");
        }
    }

    public async Task EnrollEndOfCrumb(ICrumbInnerExecutionContext context)
    {
        var utcStartTime = context.StartTime.UtcDateTime;
        var entity =
            await _client.GetEntityAsync<TableEntity>(
                utcStartTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                context.ExecutionContext.Id.ToString("D"));
        var record = CrumbRecordEntity.FromTableEntity(entity.Value);
        var duration = DateTime.UtcNow - utcStartTime;

        record = record with
        {
            ExecutionDuration = duration,
            State = context.Exception is null ? CrumbState.Completed : CrumbState.Failed,
        };

        using var response = await _client.UpsertEntityAsync(record.ToTableEntity());

        if (response.IsError)
        {
            throw new InvalidOperationException(
                $"Unable update journal table entity, with status {response.Status} and reason '{response.ReasonPhrase}'.");
        }
    }
}
