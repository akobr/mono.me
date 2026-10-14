namespace _42.Platform.Storyteller.KeyValues;

// Who writes, and what the caller may do with secrets. The API decides the rights; the service enforces them.
public sealed record KeyValueCaller(AuditActor Actor, bool CanReadSecrets, bool CanWriteSecrets, KeyValueChannel Channel = KeyValueChannel.User)
{
    public string? CorrelationId { get; init; }

    public static KeyValueCaller WithSecrets(AuditActor actor) => new(actor, CanReadSecrets: true, CanWriteSecrets: true);

    public static KeyValueCaller WithoutSecrets(AuditActor actor) => new(actor, CanReadSecrets: false, CanWriteSecrets: false);

    public static KeyValueCaller System() => new(AuditActor.System, CanReadSecrets: true, CanWriteSecrets: true, KeyValueChannel.System);
}

// The caller of the current request, for the paths that have no caller parameter (binding reads).
// The API sets it once per request; the service attributes audit events and usage to it.
public static class KeyValueCallerContext
{
    private static readonly AsyncLocal<KeyValueCaller?> Ambient = new();

    public static KeyValueCaller? Current
    {
        get => Ambient.Value;
        set => Ambient.Value = value;
    }
}
