namespace _42.Platform.Storyteller.Sdk.Configuring;

// A secret reference that a resolver accepted could not be resolved. Names the vault and the secret, never a value.
public sealed class SecretResolutionException : Exception
{
    public SecretResolutionException(string message, bool isTransient, Exception? innerException = null)
        : this(message, isTransient, null, null, innerException)
    {
    }

    public SecretResolutionException(string message, bool isTransient, string? vaultName, string? secretName, Exception? innerException = null)
        : base(message, innerException)
    {
        IsTransient = isTransient;
        VaultName = vaultName;
        SecretName = secretName;
    }

    // Worth trying again later: the vault was unreachable, throttled or failing. Not found or forbidden is definitive.
    public bool IsTransient { get; }

    public string? VaultName { get; }

    public string? SecretName { get; }
}
