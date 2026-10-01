namespace _42.Platform.Storyteller.Accessing;

public interface IBearerTokenValidator
{
    // Null means the token is not valid for this deployment.
    // Throws BearerKeyRetrievalException when the signing keys cannot be retrieved.
    Task<BearerValidationResult?> ValidateAsync(string rawToken, CancellationToken cancellationToken = default);
}
