using _42.Platform.Storyteller.Accessing.Federation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Accessing;

public sealed class TokenExchangeOptionsValidator : IValidateOptions<TokenExchangeOptions>
{
    private const string Section = TokenExchangeOptions.SectionName;
    private const int MaxActorIdLength = 64;
    private const int ClientSecretHashLength = 64;
    private const string EntraLoginPrefix = "https://login.microsoftonline.com/";
    private const string EntraV2IssuerSuffix = "/v2.0";

    private readonly IHostEnvironment _environment;
    private readonly IOptions<UserAuthenticationOptions> _userOptions;
    private readonly IOptions<FederationOptions> _federationOptions;

    public TokenExchangeOptionsValidator(
        IHostEnvironment environment,
        IOptions<UserAuthenticationOptions> userOptions,
        IOptions<FederationOptions> federationOptions)
    {
        _environment = environment;
        _userOptions = userOptions;
        _federationOptions = federationOptions;
    }

    public ValidateOptionsResult Validate(string? name, TokenExchangeOptions options)
    {
        var failures = new List<string>();

        // Actors and issuers are checked also while the exchange is off, so turning it on cannot surface old mistakes.
        if (options.Enabled && options.Actors.Count == 0)
        {
            failures.Add($"{Section}:Actors must list at least one actor when {Section}:Enabled is true.");
        }

        if (options.Actors.Count > 0 || options.AdditionalIssuers.Count > 0)
        {
            var userOptions = _userOptions.Value;
            ValidateActors(options.Actors, userOptions, failures);
            ValidateAdditionalIssuers(options.AdditionalIssuers, userOptions, failures);
        }

        if (options.TokenLifetimeMinutes is < TokenExchangeOptions.MinTokenLifetimeMinutes or > TokenExchangeOptions.MaxTokenLifetimeMinutes)
        {
            failures.Add($"{Section}:TokenLifetimeMinutes must be between {TokenExchangeOptions.MinTokenLifetimeMinutes} and {TokenExchangeOptions.MaxTokenLifetimeMinutes}.");
        }

        if (options.MaxExchangesPerSubjectTokenPerMinute is < 1 or > TokenExchangeOptions.MaxExchangesPerSubjectTokenPerMinuteLimit)
        {
            failures.Add($"{Section}:MaxExchangesPerSubjectTokenPerMinute must be between 1 and {TokenExchangeOptions.MaxExchangesPerSubjectTokenPerMinuteLimit}.");
        }

        if (options.MaxActorTokenAgeMinutes is < TokenExchangeOptions.MinActorTokenAgeMinutes or > TokenExchangeOptions.MaxActorTokenAgeMinutesLimit)
        {
            failures.Add($"{Section}:MaxActorTokenAgeMinutes must be between {TokenExchangeOptions.MinActorTokenAgeMinutes} and {TokenExchangeOptions.MaxActorTokenAgeMinutesLimit}.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private void ValidateActors(List<TokenExchangeActorOptions> actors, UserAuthenticationOptions userOptions, List<string> failures)
    {
        var apiAudiences = GetApiAudiences(userOptions);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var audienceOwners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < actors.Count; index++)
        {
            var actor = actors[index];
            var path = $"{Section}:Actors:{index}";

            if (string.IsNullOrWhiteSpace(actor.Id))
            {
                failures.Add($"{path}:Id is required.");
            }
            else if (actor.Id.Length > MaxActorIdLength || !actor.Id.All(IsUrlSafe))
            {
                // It is a client_id of form posts and the act.sub of tokens, and it reaches audit stamps.
                failures.Add($"{path}:Id must be at most {MaxActorIdLength} letters, digits, '-', '.', '_' or '~'.");
            }
            else if (!ids.Add(actor.Id))
            {
                failures.Add($"{path}:Id '{actor.Id}' is not unique; actor ids are compared ignoring case.");
            }

            ValidateAuthentication(actor.Authentication, $"{path}:Authentication", failures);
            ValidateSubjectAudiences(actor.SubjectAudiences, apiAudiences, $"{path}:SubjectAudiences", failures);
            ValidateSharedSubjectAudiences(actors, index, audienceOwners, $"{path}:SubjectAudiences", failures);

            if (actor.AllowedClientIds.Any(string.IsNullOrWhiteSpace))
            {
                failures.Add($"{path}:AllowedClientIds entries must not be blank.");
            }

            ValidateScopes(actor, $"{path}:Scopes", failures);
            ValidateRestrictions(actor, path, failures);
        }
    }

    private void ValidateAuthentication(ActorAuthenticationOptions? authentication, string path, List<string> failures)
    {
        var workloadIdentity = authentication?.WorkloadIdentity;
        var clientAssertion = authentication?.ClientAssertion;
        var clientSecretHash = authentication?.ClientSecretHash;
        var methods = (workloadIdentity is null ? 0 : 1)
            + (clientAssertion is null ? 0 : 1)
            + (string.IsNullOrWhiteSpace(clientSecretHash) ? 0 : 1);

        if (methods != 1)
        {
            failures.Add($"{path} must set exactly one of WorkloadIdentity, ClientAssertion and ClientSecretHash.");
        }

        if (workloadIdentity is not null)
        {
            if (!IsHttpsUrl(workloadIdentity.Issuer))
            {
                failures.Add($"{path}:WorkloadIdentity:Issuer must be an absolute HTTPS URL.");
            }

            if (string.IsNullOrWhiteSpace(workloadIdentity.Subject))
            {
                failures.Add($"{path}:WorkloadIdentity:Subject is required.");
            }

            if (string.IsNullOrWhiteSpace(workloadIdentity.Audience))
            {
                failures.Add($"{path}:WorkloadIdentity:Audience is required.");
            }
            else if (FederationValidation.IsForeignAudience(workloadIdentity.Audience.Trim()))
            {
                // A token for it is a credential of that service (an api://AzureADTokenExchange token signs in as every
                // app that trusts the identity), and whoever holds one could also present it here.
                failures.Add($"{path}:WorkloadIdentity:Audience {workloadIdentity.Audience} belongs to another service: a token for it is a credential there. Use an audience only Storyteller accepts, for example api://storyteller-token-exchange.");
            }
            else if (IsEntraV2Issuer(workloadIdentity.Issuer) && !Guid.TryParse(workloadIdentity.Audience.Trim(), out _))
            {
                // The token's version comes from the requestedAccessTokenVersion of the app registration it is requested
                // for, and an App ID URI is the aud of v1 tokens only: with this pair every actor token would be refused.
                failures.Add($"{path}:WorkloadIdentity:Audience must be the app's client id with the Entra v2 issuer {workloadIdentity.Issuer}: Entra v2 tokens carry the app's client id as aud. Pair an App ID URI such as {workloadIdentity.Audience} with the v1 issuer https://sts.windows.net/{{tid}}/.");
            }

            if (!string.IsNullOrEmpty(workloadIdentity.JwksUri) && !IsHttpsUrl(workloadIdentity.JwksUri))
            {
                failures.Add($"{path}:WorkloadIdentity:JwksUri must be an absolute HTTPS URL when set.");
            }
        }

        if (clientAssertion is not null)
        {
            if (!IsHttpsUrl(clientAssertion.JwksUri))
            {
                failures.Add($"{path}:ClientAssertion:JwksUri must be an absolute HTTPS URL.");
            }

            // The metadata publishes the token endpoint as a client assertion's aud. Without PublicBaseUrl that URL comes
            // from the request's host, which the exchange never accepts as aud. Read only here, so hosts without the
            // federation and actors with other proofs never resolve the federation options.
            if (string.IsNullOrWhiteSpace(_federationOptions.Value.PublicBaseUrl))
            {
                failures.Add($"{path}:ClientAssertion needs {FederationOptions.SectionName}:PublicBaseUrl: a client assertion's aud names the token endpoint the metadata publishes, or {FederationOptions.SectionName}:Issuer.");
            }
        }

        if (!string.IsNullOrWhiteSpace(clientSecretHash))
        {
            // A stored secret is the weakest proof, so it never leaves a developer's machine.
            if (!_environment.IsDevelopment())
            {
                failures.Add($"{path}:ClientSecretHash is accepted in the Development environment only; use WorkloadIdentity or ClientAssertion.");
            }

            if (clientSecretHash.Length != ClientSecretHashLength || !clientSecretHash.All(char.IsAsciiHexDigit))
            {
                failures.Add($"{path}:ClientSecretHash must be the hex SHA-256 hash of the secret ({ClientSecretHashLength} hex digits).");
            }
        }
    }

    private static void ValidateSubjectAudiences(string[] audiences, IReadOnlyList<string> apiAudiences, string path, List<string> failures)
    {
        // The audience binding: without it, a token a person gave to any app could be exchanged.
        if (audiences.Length == 0)
        {
            failures.Add($"{path} must list at least one audience (the actor's resource URL).");
            return;
        }

        if (audiences.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add($"{path} entries must not be blank.");
        }

        // A token issued for the API would then be exchangeable, and the API would refuse its own users' tokens as
        // tokens issued for an actor. Compared without a trailing '/', which the token handlers ignore when matching aud.
        foreach (var audience in audiences.Where(audience => !string.IsNullOrWhiteSpace(audience)
            && apiAudiences.Any(apiAudience => string.Equals(TrimSlash(audience), TrimSlash(apiAudience), StringComparison.OrdinalIgnoreCase))))
        {
            failures.Add($"{path} must not name the API's own audience '{audience}'.");
        }
    }

    // One resource may have several actors (one per workload identity), but only with one policy: else a token issued for
    // the restricted actor could be exchanged by the other, which would lift its restrictions. Compared as
    // IsIssuedToAnActor compares audiences.
    private static void ValidateSharedSubjectAudiences(
        List<TokenExchangeActorOptions> actors,
        int index,
        Dictionary<string, int> audienceOwners,
        string path,
        List<string> failures)
    {
        var actor = actors[index];
        var audiences = actor.SubjectAudiences
            .Where(audience => !string.IsNullOrWhiteSpace(audience))
            .Select(TokenExchangeOptions.NormalizeAudience)
            .Where(audience => audience.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var audience in audiences)
        {
            if (audienceOwners.TryAdd(audience, index))
            {
                continue;
            }

            var ownerIndex = audienceOwners[audience];
            var owner = actors[ownerIndex];

            if (!HasSamePolicy(actor, owner))
            {
                failures.Add($"{path} names '{audience}', which {Section}:Actors:{ownerIndex} ('{owner.Id}') also lists with other Restrictions, AllowSecrets or Scopes; a token issued for one actor could be exchanged by the other.");
            }
        }
    }

    private static bool HasSamePolicy(TokenExchangeActorOptions actor, TokenExchangeActorOptions other)
    {
        return actor.GetRestrictions().ToHashSet(StringComparer.Ordinal).SetEquals(other.GetRestrictions())
            && actor.GetScopes().ToHashSet(StringComparer.Ordinal).SetEquals(other.GetScopes());
    }

    private static void ValidateScopes(TokenExchangeActorOptions actor, string path, List<string> failures)
    {
        if (actor.Scopes.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add($"{path} entries must not be blank.");
        }

        foreach (var scope in actor.Scopes.Where(scope => !string.IsNullOrWhiteSpace(scope) && !IsKnownScope(scope)))
        {
            failures.Add($"{path} names '{scope}', which is not a Storyteller scope.");
        }

        // An issued token without it could not act for the person.
        if (!actor.GetScopes().Contains(UserScopes.Impersonation, StringComparer.Ordinal))
        {
            failures.Add($"{path} must contain {UserScopes.Impersonation}.");
        }
    }

    private static void ValidateRestrictions(TokenExchangeActorOptions actor, string path, List<string> failures)
    {
        foreach (var restriction in actor.Restrictions.Where(restriction => !ActorRestrictions.IsKnown(restriction)))
        {
            failures.Add($"{path}:Restrictions names '{restriction}', which is not one of {string.Join(", ", ActorRestrictions.All)}.");
        }

        if (actor.AllowSecrets && actor.Restrictions.Contains(ActorRestrictions.NoSecretReveal, StringComparer.Ordinal))
        {
            failures.Add($"{path}:AllowSecrets is true while {path}:Restrictions names {ActorRestrictions.NoSecretReveal}; set one of them.");
        }
    }

    private static void ValidateAdditionalIssuers(List<TokenExchangeIssuerOptions> issuers, UserAuthenticationOptions userOptions, List<string> failures)
    {
        if (issuers.Count == 0)
        {
            return;
        }

        // Entra ID and Keycloak subject tokens come from the API's own issuer (the tenant, the realm).
        if (userOptions.Provider != IdentityProviderKind.AuthKit)
        {
            failures.Add($"{Section}:AdditionalIssuers applies to AuthKit only (Auth:Provider is {userOptions.Provider}).");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < issuers.Count; index++)
        {
            var issuer = issuers[index];
            var path = $"{Section}:AdditionalIssuers:{index}";

            if (!IsHttpsUrl(issuer.Issuer))
            {
                failures.Add($"{path}:Issuer must be an absolute HTTPS URL.");
            }
            else if (userOptions.Provider == IdentityProviderKind.AuthKit
                && string.Equals(TrimSlash(issuer.Issuer), TrimSlash(userOptions.AuthKit.Issuer), StringComparison.OrdinalIgnoreCase))
            {
                // Its tokens already pass as bearer tokens on API routes, which an additional issuer never does.
                failures.Add($"{path}:Issuer must differ from Auth:AuthKit:Issuer.");
            }
            else if (!seen.Add(TrimSlash(issuer.Issuer)))
            {
                failures.Add($"{path}:Issuer is listed more than once.");
            }

            if (!IsHttpsUrl(issuer.JwksUri))
            {
                failures.Add($"{path}:JwksUri must be an absolute HTTPS URL.");
            }
        }
    }

    // The audiences the API's bearer validator accepts user tokens for.
    private static List<string> GetApiAudiences(UserAuthenticationOptions options)
    {
        var audiences = new List<string>();

        switch (options.Provider)
        {
            case IdentityProviderKind.EntraId when !string.IsNullOrWhiteSpace(options.ClientId):
                audiences.Add($"api://{options.ClientId.Trim()}");
                audiences.Add(options.ClientId.Trim());
                break;
            case IdentityProviderKind.AuthKit when !string.IsNullOrWhiteSpace(options.AuthKit.Audience):
                audiences.Add(options.AuthKit.Audience.Trim());
                break;
            case IdentityProviderKind.Keycloak when !string.IsNullOrWhiteSpace(options.Keycloak.Audience):
                audiences.Add(options.Keycloak.Audience.Trim());
                break;
        }

        return audiences;
    }

    private static bool IsKnownScope(string scope)
    {
        return string.Equals(scope, UserScopes.Impersonation, StringComparison.Ordinal)
            || MachineScopes.TryParseScopeString(scope, out _);
    }

    // RFC 3986 unreserved characters.
    private static bool IsUrlSafe(char character)
    {
        return char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '~';
    }

    // The issuer of Entra v2 tokens, https://login.microsoftonline.com/{tid}/v2.0, whose aud is always a client id.
    private static bool IsEntraV2Issuer(string? issuer)
    {
        var trimmed = issuer?.Trim().TrimEnd('/') ?? string.Empty;
        return trimmed.StartsWith(EntraLoginPrefix, StringComparison.OrdinalIgnoreCase)
            && trimmed.EndsWith(EntraV2IssuerSuffix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHttpsUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
    }

    private static string TrimSlash(string value)
    {
        return value.Trim().TrimEnd('/');
    }
}
