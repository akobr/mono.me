namespace _42.Platform.Storyteller.Accessing;

// Validates machine (client credentials) tokens of the identity provider registered for machine
// access. BearerAuthenticationMiddleware asks it after the user provider's IBearerTokenValidator
// rejected a token, so machines may come from a different provider than users.
public interface IMachineTokenValidator
{
    // True when tokens from this (unverified) issuer belong to this provider.
    bool CanValidate(string issuer);

    // Null when the token is not a valid machine token of this provider.
    // Throws BearerKeyRetrievalException when the signing keys cannot be retrieved.
    Task<BearerValidationResult?> ValidateAsync(string rawToken, CancellationToken cancellationToken = default);
}
