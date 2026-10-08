using System.Security.Claims;

namespace _42.Platform.Storyteller.Accessing;

public interface IBearerClaimsNormalizer
{
    // Maps the claims of a token from the configured provider to the claim set the API reads.
    // Validators call it after a successful check. DEV_AUTH builds call it on decoded, unvalidated claims.
    BearerValidationResult Normalize(IReadOnlyList<Claim> claims);
}
