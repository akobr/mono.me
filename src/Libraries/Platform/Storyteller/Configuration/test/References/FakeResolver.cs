namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.References;

// A resolver of a made-up secret store that is not Azure: it accepts the values in Secrets, or what Accepts says, and
// answers from Secrets unless Respond answers. Records what it was asked and what it resolved.
internal sealed class FakeResolver : ISecretReferenceResolver
{
    private readonly List<string> _asked = [];
    private readonly List<string> _resolved = [];
    private readonly Lock _lock = new();

    public Dictionary<string, string> Secrets { get; } = new(StringComparer.Ordinal);

    public Func<string, bool>? Accepts { get; set; }

    public Func<string, CancellationToken, Task<string>>? Respond { get; set; }

    public IReadOnlyList<string> Asked
    {
        get
        {
            lock (_lock)
            {
                return [.. _asked];
            }
        }
    }

    public IReadOnlyList<string> Resolved
    {
        get
        {
            lock (_lock)
            {
                return [.. _resolved];
            }
        }
    }

    public bool CanResolve(string value)
    {
        lock (_lock)
        {
            _asked.Add(value);
        }

        return Accepts?.Invoke(value) ?? Secrets.ContainsKey(value);
    }

    public async ValueTask<string> ResolveAsync(string value, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _resolved.Add(value);
        }

        return Respond is { } respond
            ? await respond(value, cancellationToken)
            : Secrets[value];
    }
}
