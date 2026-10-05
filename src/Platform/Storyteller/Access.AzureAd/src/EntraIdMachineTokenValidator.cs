using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller;

// Entra ID client-credentials tokens of machine app registrations, for deployments whose user
// provider is not Entra ID. The checks are EntraIdBearerTokenValidator's (audiences api://{ClientId}
// and {ClientId}, Microsoft issuers, azp/appid other than the API application); only machine
// results are returned.
public class EntraIdMachineTokenValidator : IMachineTokenValidator
{
    private readonly EntraIdBearerTokenValidator _validator;

    public EntraIdMachineTokenValidator(EntraIdBearerTokenValidator validator)
    {
        _validator = validator;
    }

    public bool CanValidate(string issuer)
    {
        return EntraIdBearerTokenValidator.IsEntraIssuer(issuer);
    }

    public async Task<BearerValidationResult?> ValidateAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        var result = await _validator.ValidateAsync(rawToken, cancellationToken);
        return result is { IsMachine: true } ? result : null;
    }
}
