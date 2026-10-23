using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Configuring.References;

// The substitution step of the apply pipeline: after flattening and validation, before the swap. Phase 2 of the spec
// leaves every document as it is; Phase 3 walks the string values in Resolved mode, resolves what the resolver accepts,
// keeps resolved values in memory per value text until SecretRefreshInterval, and fails the apply on a failure.
internal sealed class ReferenceSubstitution
{
    private readonly ISecretReferenceResolver? _resolver;
    private readonly StorytellerReadMode _mode;
    private readonly TimeSpan _refreshInterval;
    private readonly TimeProvider _time;

    public ReferenceSubstitution(ISecretReferenceResolver? resolver, StorytellerReadMode mode, TimeSpan refreshInterval, TimeProvider time)
    {
        _resolver = resolver;
        _mode = mode;
        _refreshInterval = refreshInterval;
        _time = time;
    }

    // The content to flatten and apply. Never changes serverContent: the cache keeps the server's document. The token
    // bounds resolver calls only: a startup that falls back to the cache after its read used up ReadTimeout still
    // applies an entry that needs no resolution.
    public ValueTask<SubstitutionResult> SubstituteAsync(JObject serverContent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serverContent);

        return ValueTask.FromResult(new SubstitutionResult(serverContent, null));
    }

    // Step 2 of a run: whether a 304 must resolve the snapshot's references again. An infinite interval never expires.
    public bool IsRefreshDue(SubstitutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.OldestResolution is { } resolvedAt
            && _resolver is not null
            && _mode == StorytellerReadMode.Resolved
            && _refreshInterval != Timeout.InfiniteTimeSpan
            && _time.GetUtcNow() - resolvedAt >= _refreshInterval;
    }
}
