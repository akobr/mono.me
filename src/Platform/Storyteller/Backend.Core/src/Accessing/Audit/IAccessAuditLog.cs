namespace _42.Platform.Storyteller.Accessing;

// The access audit trail of an organization. Only a read: every record is written by the change it records, in the same
// transaction, and nothing updates or deletes one; they expire by their ttl (AccessAudit:RetentionDays).
public interface IAccessAuditLog
{
    // Newest first. InvalidInputException (InvalidQuery) for a continuation token this log did not issue.
    Task<AccessAuditResponse> QueryAsync(string organization, AccessAuditQuery query, CancellationToken cancellationToken = default);
}
