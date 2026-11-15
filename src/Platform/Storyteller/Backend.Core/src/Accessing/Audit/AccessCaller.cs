namespace _42.Platform.Storyteller.Accessing;

// Who changes access, and the request it came with: the actor and the correlation id of the access audit record. The API
// builds it from the request; DbCreator and the timers use System.
public sealed record AccessCaller(AuditActor Actor)
{
    public string? CorrelationId { get; init; }

    public static AccessCaller System { get; } = new(AuditActor.System);

    public static AccessCaller Account(string accountId, string? name = null) => new(AuditActor.Account(accountId, name));
}
