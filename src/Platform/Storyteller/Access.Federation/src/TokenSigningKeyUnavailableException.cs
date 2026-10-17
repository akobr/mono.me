namespace _42.Platform.Storyteller;

// No signing key can be used: none is stored and this instance may not create one
// (MachineAuth:Federation:SigningKeyAutoBootstrap), or the store cannot be read. The token endpoint answers it with 503.
public sealed class TokenSigningKeyUnavailableException : InvalidOperationException
{
    public TokenSigningKeyUnavailableException(string message)
        : base(message)
    {
    }

    public TokenSigningKeyUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
