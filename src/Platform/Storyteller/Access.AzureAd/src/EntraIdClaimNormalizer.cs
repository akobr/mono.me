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

    // The validator's rules, so the decoded and the validated paths agree: machines (another azp or appid) keep their
    // claims as they are, and users are keyed by {tid}:{oid}. A user without tid and oid keeps no sub, so
    // GetIdentityUniqueId answers 401.
    public BearerValidationResult Normalize(IReadOnlyList<Claim> claims)
    {
        var machineId = EntraIdBearerTokenValidator.TryGetMachineId(claims, _clientId);

        return machineId is not null
            ? new BearerValidationResult(claims, true, machineId)
            : new BearerValidationResult(EntraIdBearerTokenValidator.KeyByAccountId(claims, out _), false, null);
    }
}
