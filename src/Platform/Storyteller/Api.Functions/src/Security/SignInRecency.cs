using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller.Api.Security;

// How recent the sign-in of the user of a request is, judged once by BearerAuthenticationMiddleware against Auth:StepUp
// and kept as the item SignInRecency. Handlers read it through HttpRequestDataExtensions.GetSignInRecency; machines
// never have one. IsRecent is true while the step-up is off.
public sealed record SignInRecency(bool IsRecent, TimeSpan MaxAge)
{
    public static SignInRecency NotRequired(TimeSpan maxAge) => new(IsRecent: true, maxAge);

    public static SignInRecency Stale(TimeSpan maxAge) => new(IsRecent: false, maxAge);

    // The sign-in counts when the token's auth_time is at most MaxAge old and at most ClockSkew in the future. A token
    // without a readable auth_time has no sign-in time, so it never counts, and neither does a token that names an actor
    // (act, a WorkOS impersonation): its sign-in is the impersonator's, not the person's. A Storyteller user token keeps
    // its actor in the delegation, never in the claims.
    public static SignInRecency Evaluate(
        IReadOnlyList<Claim> claims,
        StepUpOptions options,
        IdentityProviderKind provider,
        DateTimeOffset now,
        bool developmentAuthentication)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.IsEnabledFor(provider, developmentAuthentication))
        {
            return NotRequired(options.MaxAge);
        }

        if (UserSubjectTokenRules.HasActor(claims) || UserSubjectTokenRules.GetAuthTime(claims) is not { } authTime)
        {
            return Stale(options.MaxAge);
        }

        var age = now - authTime;
        return age <= options.MaxAge && age >= -StepUpOptions.ClockSkew
            ? new SignInRecency(IsRecent: true, options.MaxAge)
            : Stale(options.MaxAge);
    }
}
