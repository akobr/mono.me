using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

public sealed class EntraIdClaimNormalizer : IBearerClaimsNormalizer
{
    private readonly string? _clientId;

    public EntraIdClaimNormalizer(IOptions<UserAuthenticationOptions> options)
    {
        _clientId = options.Value.ClientId;
    }

    // Entra claims already have the shape the API reads. Only the machine rule applies.
    public BearerValidationResult Normalize(IReadOnlyList<Claim> claims)
    {
        var machineId = EntraIdBearerTokenValidator.TryGetMachineId(claims, _clientId);
        return new BearerValidationResult(claims, machineId is not null, machineId);
    }
}
