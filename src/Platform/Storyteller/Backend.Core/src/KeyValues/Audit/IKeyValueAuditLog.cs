namespace _42.Platform.Storyteller.KeyValues.Audit;

// The audit trail of a project's secrets. Management operations and reveals are written at once;
// binding reads are counted per actor, key and hour and written in one event by the flush.
public interface IKeyValueAuditLog
{
    Task WriteAsync(string organization, string project, KeyValueAuditEvent entry, CancellationToken cancellationToken = default);

    void RecordBindingRead(string organization, string project, string view, string key, KeyValueProtection protection, KeyValueCaller caller);

    Task FlushAsync(CancellationToken cancellationToken = default);

    Task<KeyValueAuditResponse> QueryAsync(string organization, string project, KeyValueAuditQuery query, CancellationToken cancellationToken = default);
}
