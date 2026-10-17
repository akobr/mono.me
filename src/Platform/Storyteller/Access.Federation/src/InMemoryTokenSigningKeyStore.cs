using _42.Platform.Storyteller.Accessing.Federation;

namespace _42.Platform.Storyteller;

// The signing key of one instance, the default when Cosmos DB is not registered (tests, local runs).
// Tokens it signs are valid only on this instance.
public class InMemoryTokenSigningKeyStore : ITokenSigningKeyStore
{
    private TokenSigningKey? _key;

    public Task<TokenSigningKey?> GetAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Volatile.Read(ref _key));
    }

    public Task<bool> TryCreateAsync(TokenSigningKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Task.FromResult(Interlocked.CompareExchange(ref _key, key, null) is null);
    }
}
