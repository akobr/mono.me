using _42.Platform.Storyteller.Sdk.Configuring;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Subscriptions;

// A resolver of a made-up secret store: it accepts the references in Secrets and answers their current values.
internal sealed class TestResolver : ISecretReferenceResolver
{
    public Dictionary<string, string> Secrets { get; } = new(StringComparer.Ordinal);

    public bool CanResolve(string value)
    {
        lock (Secrets)
        {
            return Secrets.ContainsKey(value);
        }
    }

    public ValueTask<string> ResolveAsync(string value, CancellationToken cancellationToken)
    {
        lock (Secrets)
        {
            return ValueTask.FromResult(Secrets[value]);
        }
    }
}
