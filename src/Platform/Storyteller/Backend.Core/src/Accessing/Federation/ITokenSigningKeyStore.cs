namespace _42.Platform.Storyteller.Accessing.Federation;

// The one signing key of Storyteller tokens, shared by every instance. Deleting it is the emergency rotation.
public interface ITokenSigningKeyStore
{
    // Null while no key is stored.
    Task<TokenSigningKey?> GetAsync(CancellationToken cancellationToken = default);

    // Stores the key unless one exists. False when another instance stored one first; read that one instead.
    Task<bool> TryCreateAsync(TokenSigningKey key, CancellationToken cancellationToken = default);
}
