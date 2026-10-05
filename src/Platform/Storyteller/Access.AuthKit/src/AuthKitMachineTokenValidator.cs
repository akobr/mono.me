using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller;

// Validates AuthKit M2M tokens: issuer AuthKitDomain, JWKS {AuthKitDomain}/oauth2/jwks, audience
// the environment client ID (WorkOS does not let M2M applications choose it), sub == client_id and
// org_id == MachineOrganizationId. Registered by AddAuthKitMachineAccess.
public class AuthKitMachineTokenValidator : IMachineTokenValidator
{
    private readonly AuthKitTokenSource _source;
    private readonly string _machineOrganizationId;
    private readonly AuthKitClaimNormalizer _normalizer;

    public AuthKitMachineTokenValidator(IOptions<UserAuthenticationOptions> options, AuthKitClaimNormalizer normalizer)
        : this(options, normalizer, AuthKitTokenSource.CreateKeyManager(options.Value.AuthKit.GetMachineJwksUri()))
    {
    }

    public AuthKitMachineTokenValidator(
        IOptions<UserAuthenticationOptions> options,
        AuthKitClaimNormalizer normalizer,
        IConfigurationManager<JsonWebKeySet> keyManager)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(normalizer);
        ArgumentNullException.ThrowIfNull(keyManager);

        var authKit = options.Value.AuthKit;

        if (!authKit.HasMachineAccess() || string.IsNullOrWhiteSpace(authKit.ClientId))
        {
            throw new InvalidOperationException(
                "Auth:AuthKit:AuthKitDomain, MachineOrganizationId and ClientId are required for AuthKit machine tokens.");
        }

        _source = new AuthKitTokenSource(authKit.AuthKitDomain, authKit.ClientId, keyManager);
        _machineOrganizationId = authKit.MachineOrganizationId!;
        _normalizer = normalizer;
    }

    public bool CanValidate(string issuer)
    {
        return AuthKitTokenSource.IssuerMatches(issuer, _source.Issuer);
    }

    public async Task<BearerValidationResult?> ValidateAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        if (!CanValidate(AuthKitTokenSource.TryReadIssuer(rawToken) ?? string.Empty))
        {
            return null;
        }

        var claims = await _source.ValidateAsync(rawToken, cancellationToken);

        if (claims is null)
        {
            return null;
        }

        // Connect user tokens from the AuthKit domain are not accepted (yet); only applications.
        var clientId = AuthKitClaimNormalizer.TryGetMachineClientId(claims);

        // Defense in depth: Storyteller creates every M2M application in the machine organization.
        var organizationId = claims.FirstOrDefault(claim => claim.Type == "org_id")?.Value;

        return clientId is not null && string.Equals(organizationId, _machineOrganizationId, StringComparison.Ordinal)
            ? _normalizer.NormalizeMachine(claims, clientId)
            : null;
    }
}
