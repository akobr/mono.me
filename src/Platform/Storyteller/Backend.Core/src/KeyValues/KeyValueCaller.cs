namespace _42.Platform.Storyteller.KeyValues;

// The author of a write, and what the caller may do with secrets. The API decides the rights; the service enforces them.
public sealed record KeyValueCaller(string Author, bool CanReadSecrets, bool CanWriteSecrets, KeyValueChannel Channel = KeyValueChannel.User)
{
    public string? CorrelationId { get; init; }

    public static KeyValueCaller WithSecrets(string author) => new(author, CanReadSecrets: true, CanWriteSecrets: true);

    public static KeyValueCaller WithoutSecrets(string author) => new(author, CanReadSecrets: false, CanWriteSecrets: false);

    public static KeyValueCaller System(string author = "system") => new(author, CanReadSecrets: true, CanWriteSecrets: true, KeyValueChannel.System);
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
