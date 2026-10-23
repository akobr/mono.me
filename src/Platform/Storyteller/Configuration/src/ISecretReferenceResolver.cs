namespace _42.Platform.Storyteller.Sdk.Configuring;

// Resolves the secret references in the string values of a resolved document, locally, with the application's own
// identity. Neutral about vaults: Azure Key Vault is one implementation, others can follow.
public interface ISecretReferenceResolver
{
    // value is the whole, trimmed string value. false leaves the value to the next resolver, or as it is.
    bool CanResolve(string value);

    // Throws SecretResolutionException, with its class, when an accepted value cannot be resolved; any other exception
    // counts as definitive. The resolved value is never logged, cached on disk or put in an exception.
    ValueTask<string> ResolveAsync(string value, CancellationToken cancellationToken);
}
