using _42.Platform.Storyteller.Accessing.Federation;

namespace _42.Platform.Storyteller.Access.Federation.UnitTests;

// A signing key store that counts its reads and writes. Shared between providers, it plays the core container
// that every instance sees.
internal sealed class CountingSigningKeyStore : ITokenSigningKeyStore
{
    private int _reads;
    private int _creates;

    public TokenSigningKey? Key { get; set; }

    // Stored by "another instance" just before this one's create, so the create conflicts.
    public TokenSigningKey? Winner { get; set; }

    public Exception? ReadError { get; set; }

    public int Reads => _reads;

    public int Creates => _creates;

    public Task<TokenSigningKey?> GetAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _reads);
        return ReadError is { } error ? Task.FromException<TokenSigningKey?>(error) : Task.FromResult(Key);
    }

    public Task<bool> TryCreateAsync(TokenSigningKey key, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _creates);

        if (Winner is not null)
        {
            Key = Winner;
            return Task.FromResult(false);
        }

        if (Key is not null)
        {
            return Task.FromResult(false);
        }

        Key = key;
        return Task.FromResult(true);
    }
}
