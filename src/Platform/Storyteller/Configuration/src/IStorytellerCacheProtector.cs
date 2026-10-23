namespace _42.Platform.Storyteller.Sdk.Configuring;

// Encrypts the entries of the last-known-good cache. Must be authenticated encryption with the identity as its purpose
// or associated data, so a blob moved to the file of another identity does not unprotect. With a protector, every entry
// is protected, also the responses marked no-store.
public interface IStorytellerCacheProtector
{
    byte[] Protect(byte[] plaintext, string identity);

    // Throws when the data was not protected for this identity or was changed.
    byte[] Unprotect(byte[] protectedData, string identity);
}
