using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

public sealed class EntraIdClaimNormalizer : IBearerClaimsNormalizer
{
    private readonly string? _clientId;
    private readonly IReadOnlyList<DelegatedClientOptions> _delegatedClients;

    public EntraIdClaimNormalizer(IOptions<UserAuthenticationOptions> options)
    {
        _clientId = options.Value.ClientId;
        _delegatedClients = options.Value.DelegatedClients.ToArray();
    }

    // Entra claims already have the shape the API reads. Only the machine rule applies, with the same delegated
    // clients as the validator, so the decoded and the validated paths agree.
    public BearerValidationResult Normalize(IReadOnlyList<Claim> claims)
    {
        var machineId = EntraIdBearerTokenValidator.TryGetMachineId(claims, _clientId, _delegatedClients);
        return new BearerValidationResult(claims, machineId is not null, machineId);
    }
}
