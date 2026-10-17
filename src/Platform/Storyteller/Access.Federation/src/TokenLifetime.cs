using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller;

internal static class TokenLifetime
{
    // The lifetime check of JsonWebTokenHandler against the injected clock: `exp` required, `nbf` and `exp` with the skew.
    public static LifetimeValidator CreateValidator(TimeProvider timeProvider, TimeSpan clockSkew)
    {
        return (notBefore, expires, _, _) =>
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;

            return expires is { } validTo
                && validTo.ToUniversalTime() > now - clockSkew
                && (notBefore is not { } validFrom || validFrom.ToUniversalTime() <= now + clockSkew);
        };
    }
}
