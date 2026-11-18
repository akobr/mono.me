namespace _42.Platform.Storyteller.Accessing;

// The step-up of users (section Auth:StepUp, RFC 9470): reading or writing secret values, changing roles and members,
// and creating or resetting machine credentials need a sign-in at most MaxAge old (the token's auth_time). Machines are
// never affected. Bound and checked at start by AddUserAuthenticationOptions.
public class StepUpOptions
{
    public const string SectionName = "Auth:StepUp";

    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan MinMaxAge = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan MaxMaxAge = TimeSpan.FromHours(24);

    // A sign-in this far in the future still counts (the provider's clock may be ahead of the API's); further, it is no
    // sign-in at all.
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    // Null: on under AuthKit, whose access tokens carry auth_time and whose clients (the admin UI) can ask for a new sign-in
    // with max_age; off under Entra ID, whose access tokens carry auth_time only when the API registration adds it as an
    // optional claim (not by default), and under Keycloak, where no client of the platform can ask for one yet; off in
    // Debug builds (DEV_AUTH) whatever the provider. True or false decides in every build; true refuses every user token
    // without auth_time.
    public bool? Enabled { get; set; }

    // The oldest sign-in that still counts, in whole seconds.
    public TimeSpan MaxAge { get; set; } = DefaultMaxAge;

    public bool IsEnabledFor(IdentityProviderKind provider, bool developmentAuthentication)
    {
        return Enabled ?? (!developmentAuthentication && provider == IdentityProviderKind.AuthKit);
    }
}
