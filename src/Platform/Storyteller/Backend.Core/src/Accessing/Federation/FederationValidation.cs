using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace _42.Platform.Storyteller.Accessing.Federation;

// The rules of trusted issuers and federation rules. Pure: each check returns null or the 400 message,
// so the API, the machine router, the token exchange and the tests share them.
public static partial class FederationValidation
{
    public const int MaxIssuersPerProject = 20;
    public const int MaxRulesPerMachine = 10;
    public const int MaxIssuerLength = 512;
    public const int MaxAudienceLength = 512;
    public const int MaxSubjectLength = 512;
    public const int MaxClaimKeys = 10;
    public const int MaxClaimValues = 10;
    public const int MaxClaimValueLength = 512;
    public const int MaxDescriptionLength = 256;
    public const int MaxJwksBytes = 16 * 1024;
    public const int MaxJwksKeys = 10;

    // The range of FederatedIssuer.MaxTokenLifetimeSeconds, and the value a null one means.
    public const int MinTokenLifetimeSeconds = 60;
    public const int MaxTokenLifetimeSeconds = 86400;
    public const int DefaultMaxTokenLifetimeSeconds = 3600;

    public const string IssuerImmutableMessage = "The issuer of a trusted issuer cannot change; create a new one.";
    public const string RulesOnOtherKindMessage = "Only Federated machines have federation rules.";

    public static readonly string TooManyIssuersMessage = $"A project has at most {MaxIssuersPerProject} trusted issuers.";

    private const string GitHubHost = "token.actions.githubusercontent.com";
    private const string GitHubEnterpriseHostPrefix = "token.actions.";
    private const string GitHubEnterpriseHostSuffix = ".ghe.com";

    // Audiences of other relying parties: a token minted for one of them must not be replayable here.
    private static readonly string[] ForeignAudiences =
    [
        "sts.amazonaws.com",
        "pods.eks.amazonaws.com",
        "api://AzureADTokenExchange",
        "https://kubernetes.default.svc",
        "https://kubernetes.default.svc.cluster.local",
        "https://management.azure.com",
        "https://management.core.windows.net",
        "https://graph.microsoft.com",
        "https://vault.azure.net",
        "https://storage.azure.com",
        "https://database.windows.net",
    ];

    // Google workload identity pools, and the defaults of GitHub (https://github.com/<owner>) and Vercel (https://vercel.com/<team>).
    private static readonly string[] ForeignAudiencePrefixes =
    [
        "//iam.googleapis.com/",
        "https://iam.googleapis.com/",
        "https://github.com/",
        "https://vercel.com/",
    ];

    private static readonly string[] PrivateJwkMembers = ["d", "p", "q", "dp", "dq", "qi", "k"];
    private static readonly string[] GitHubIdClaims = ["repository_owner_id", "repository_id", "enterprise_id"];
    private static readonly string[] VercelIdClaims = ["owner_id"];

    // Null when the issuer can be stored, else why not. Does not know the other issuers (see ValidateIssuerChange).
    public static string? ValidateIssuer(FederatedIssuer? issuer, FederationOptions options)
    {
        if (issuer is null)
        {
            return "The trusted issuer is missing.";
        }

        if (!IsValidName(issuer.Name))
        {
            return $"The name '{issuer.Name}' of a trusted issuer is not valid: use 1 to 63 lower-case letters, digits or hyphens, starting and ending with a letter or a digit.";
        }

        if (!Enum.IsDefined(issuer.KeySource))
        {
            return $"The key source {issuer.KeySource:D} doesn't exist.";
        }

        if (string.IsNullOrEmpty(issuer.Issuer))
        {
            return "The issuer is required.";
        }

        if (issuer.Issuer.Length > MaxIssuerLength)
        {
            return $"The issuer can have at most {MaxIssuerLength} characters.";
        }

        // Storyteller's own tokens (machine tokens, and user tokens of the exchange) are never subject tokens: a user token
        // with an actor would become a machine token without the actor's restrictions, and a machine token of one project
        // a machine of another.
        if (IsOwnIssuer(issuer.Issuer, options))
        {
            return $"The issuer {issuer.Issuer} is Storyteller itself; its tokens cannot be exchanged for machine tokens.";
        }

        // Only a discovered issuer is fetched; with JwksUri or Jwks it is just compared with `iss`.
        if (issuer.KeySource == FederatedIssuerKeySource.Discovery
            && ValidateMetadataUrl(issuer.Issuer, options.AllowPrivateNetworks, allowQuery: false) is { } issuerError)
        {
            return $"The issuer {issuer.Issuer} cannot be discovered: {issuerError}";
        }

        if (ValidateKeySourceFields(issuer, options) is { } keyError)
        {
            return keyError;
        }

        if (issuer.MaxTokenLifetimeSeconds is { } lifetime
            && lifetime is < MinTokenLifetimeSeconds or > MaxTokenLifetimeSeconds)
        {
            return $"The maximum token lifetime must be between {MinTokenLifetimeSeconds} and {MaxTokenLifetimeSeconds} seconds.";
        }

        if (ValidateClaims(issuer.RequiredClaims) is { } claimsError)
        {
            return $"Required claims: {claimsError}";
        }

        if (issuer.Description is { Length: > MaxDescriptionLength })
        {
            return $"The description can have at most {MaxDescriptionLength} characters.";
        }

        return null;
    }

    // True when issuer is MachineAuth:Federation:Issuer, ignoring case and one trailing '/'. False while that is not set.
    public static bool IsOwnIssuer(string? issuer, FederationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return !string.IsNullOrEmpty(issuer)
            && !string.IsNullOrEmpty(options.Issuer)
            && string.Equals(TrimOneTrailingSlash(issuer), TrimOneTrailingSlash(options.Issuer), StringComparison.OrdinalIgnoreCase);
    }

    // Null when the issuer can be stored next to the project's current issuers: an existing name keeps its Issuer
    // (rules were written for it), a new name needs room.
    public static string? ValidateIssuerChange(FederatedIssuer requested, IReadOnlyCollection<FederatedIssuer> currentIssuers)
    {
        var existing = currentIssuers.FirstOrDefault(issuer => string.Equals(issuer.Name, requested.Name, StringComparison.Ordinal));

        if (existing is not null)
        {
            return string.Equals(existing.Issuer, requested.Issuer, StringComparison.Ordinal)
                ? null
                : IssuerImmutableMessage;
        }

        return currentIssuers.Count >= MaxIssuersPerProject ? TooManyIssuersMessage : null;
    }

    // Null when the rules of a Federated machine can be stored, else why not, naming the rule ("Rule 2: …").
    public static string? ValidateRules(IReadOnlyList<FederationRule>? rules, IReadOnlyCollection<FederatedIssuer> projectIssuers)
    {
        if (rules is null || rules.Count < 1)
        {
            return "A Federated machine needs at least one federation rule.";
        }

        if (rules.Count > MaxRulesPerMachine)
        {
            return $"A machine has at most {MaxRulesPerMachine} federation rules.";
        }

        for (var index = 0; index < rules.Count; index++)
        {
            var number = index + 1;
            var rule = rules[index];

            if (rule is null)
            {
                return $"Rule {number} is missing.";
            }

            if (string.IsNullOrEmpty(rule.Issuer))
            {
                return $"Rule {number}: the trusted issuer is required.";
            }

            var issuer = projectIssuers.FirstOrDefault(candidate => string.Equals(candidate.Name, rule.Issuer, StringComparison.Ordinal));

            if (issuer is null)
            {
                return $"Rule {number}: the trusted issuer '{rule.Issuer}' doesn't exist in the project.";
            }

            if (ValidateRuleAgainst(rule, issuer) is { } error)
            {
                return $"Rule {number}: {error}";
            }
        }

        return null;
    }

    // The issuer-dependent checks of one rule, also run at exchange time against the current issuer,
    // so a rule an issuer change made unsafe stops matching.
    public static string? ValidateRule(FederationRule rule, FederatedIssuer issuer)
    {
        var error = ValidateRuleAgainst(rule, issuer);
        return error is null ? null : char.ToUpperInvariant(error[0]) + error[1..];
    }

    public static SharedIssuerKind ClassifyIssuer(string? issuer)
    {
        var host = GetHost(issuer);

        if (host is null)
        {
            return SharedIssuerKind.None;
        }

        if (host == GitHubHost
            || (host.Length > GitHubEnterpriseHostPrefix.Length + GitHubEnterpriseHostSuffix.Length
                && host.StartsWith(GitHubEnterpriseHostPrefix, StringComparison.Ordinal)
                && host.EndsWith(GitHubEnterpriseHostSuffix, StringComparison.Ordinal)))
        {
            return SharedIssuerKind.GitHub;
        }

        return host switch
        {
            "oidc.vercel.com" => SharedIssuerKind.Vercel,
            "accounts.google.com" => SharedIssuerKind.Google,
            "login.microsoftonline.com" or "sts.windows.net" => SharedIssuerKind.Entra,
            _ => SharedIssuerKind.None,
        };
    }

    // The rules of every URL Storyteller fetches issuer metadata from (a discovered issuer, a JWKS URI, the jwks_uri
    // of a discovery document): absolute https on port 443, a DNS host in ASCII (no IP literal in any notation,
    // such as https://2130706433/ or https://0x7f.1/), no user info and no fragment. Private networks lift https,
    // the port and the DNS host. Null when it passes, else why not (a clause that starts in lower case).
    public static string? ValidateMetadataUrl(string? url, bool allowPrivateNetworks, bool allowQuery = true)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "it must be an absolute URL.";
        }

        if (uri.Scheme != Uri.UriSchemeHttps && !(allowPrivateNetworks && uri.Scheme == Uri.UriSchemeHttp))
        {
            return "it must use https.";
        }

        // Raw characters, as Uri normalizes an empty query or fragment away.
        if (url.Contains('#') || (!allowQuery && url.Contains('?')))
        {
            return allowQuery ? "it must not have a fragment." : "it must not have a query or a fragment.";
        }

        if (HasUserInfo(url, uri))
        {
            return "it must not carry user information.";
        }

        if (!allowPrivateNetworks && uri.Port != 443)
        {
            return "it must use port 443.";
        }

        if (uri.HostNameType != UriHostNameType.Dns)
        {
            return allowPrivateNetworks && uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
                ? null
                : "its host must be a DNS name, not an IP address.";
        }

        if (!string.Equals(uri.IdnHost, uri.Host, StringComparison.Ordinal))
        {
            return "its host must be ASCII (use the punycode form).";
        }

        return null;
    }

    public static bool IsValidName(string? name)
    {
        return name is not null && NamePattern().IsMatch(name);
    }

    private static string? ValidateKeySourceFields(FederatedIssuer issuer, FederationOptions options)
    {
        var hasJwksUri = !string.IsNullOrEmpty(issuer.JwksUri);
        var hasJwks = !string.IsNullOrEmpty(issuer.Jwks);

        if (issuer.KeySource == FederatedIssuerKeySource.JwksUri)
        {
            if (!hasJwksUri)
            {
                return "The JWKS URI is required when the key source is JwksUri.";
            }

            if (ValidateMetadataUrl(issuer.JwksUri, options.AllowPrivateNetworks) is { } uriError)
            {
                return $"The JWKS URI {issuer.JwksUri} is not valid: {uriError}";
            }
        }
        else if (hasJwksUri)
        {
            return "A JWKS URI is allowed only when the key source is JwksUri.";
        }

        if (issuer.KeySource == FederatedIssuerKeySource.Jwks)
        {
            if (!hasJwks)
            {
                return "The JWKS is required when the key source is Jwks.";
            }

            if (ValidateJwks(issuer.Jwks!) is { } jwksError)
            {
                return $"The JWKS is not valid: {jwksError}";
            }
        }
        else if (hasJwks)
        {
            return "A JWKS is allowed only when the key source is Jwks.";
        }

        return null;
    }

    // A JSON Web Key Set with 1 to 10 public RSA or EC signing keys, each with a distinct kid.
    private static string? ValidateJwks(string jwks)
    {
        if (Encoding.UTF8.GetByteCount(jwks) > MaxJwksBytes)
        {
            return $"it can have at most {MaxJwksBytes / 1024} KiB.";
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(jwks);
        }
        catch (JsonException)
        {
            return "it is not JSON.";
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("keys", out var keys)
                || keys.ValueKind != JsonValueKind.Array)
            {
                return "it needs a keys array.";
            }

            if (keys.GetArrayLength() is < 1 or > MaxJwksKeys)
            {
                return $"it needs 1 to {MaxJwksKeys} keys.";
            }

            var kids = new HashSet<string>(StringComparer.Ordinal);
            var number = 0;

            foreach (var key in keys.EnumerateArray())
            {
                number++;

                if (key.ValueKind != JsonValueKind.Object)
                {
                    return $"key {number} is not an object.";
                }

                if (PrivateJwkMembers.FirstOrDefault(member => key.TryGetProperty(member, out _)) is { } privateMember)
                {
                    return $"key {number} has the private member {privateMember}; paste public keys only.";
                }

                if (GetString(key, "kid") is not { } kid)
                {
                    return $"key {number} has no kid.";
                }

                if (!kids.Add(kid))
                {
                    return $"the kid {kid} appears more than once.";
                }

                var error = GetString(key, "kty") switch
                {
                    "RSA" => GetString(key, "n") is null || GetString(key, "e") is null
                        ? $"the RSA key {kid} needs n and e."
                        : null,
                    "EC" => GetString(key, "crv") is not ("P-256" or "P-384" or "P-521") || GetString(key, "x") is null || GetString(key, "y") is null
                        ? $"the EC key {kid} needs crv P-256, P-384 or P-521, x and y."
                        : null,
                    _ => $"the key {kid} is not an RSA or EC key.",
                };

                if (error is not null)
                {
                    return error;
                }
            }
        }

        return null;
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 } text
            ? text
            : null;
    }

    // Shared by FederationRule.Claims and FederatedIssuer.RequiredClaims. A clause that starts in lower case.
    private static string? ValidateClaims(IReadOnlyDictionary<string, IReadOnlyList<string>>? claims)
    {
        if (claims is null)
        {
            return null;
        }

        if (claims.Count > MaxClaimKeys)
        {
            return $"at most {MaxClaimKeys} claims can be pinned.";
        }

        foreach (var (key, values) in claims)
        {
            if (FederationClaimKeys.Validate(key) is { } keyError)
            {
                return keyError;
            }

            if (values is null || values.Count is < 1 or > MaxClaimValues)
            {
                return $"the claim {key} needs 1 to {MaxClaimValues} accepted values.";
            }

            if (values.Any(value => string.IsNullOrEmpty(value) || value.Length > MaxClaimValueLength))
            {
                return $"each accepted value of the claim {key} needs 1 to {MaxClaimValueLength} characters.";
            }
        }

        return null;
    }

    // A clause that starts in lower case; ValidateRules prefixes the rule number, ValidateRule capitalizes it.
    private static string? ValidateRuleAgainst(FederationRule rule, FederatedIssuer issuer)
    {
        if (!string.Equals(rule.Issuer, issuer.Name, StringComparison.Ordinal))
        {
            return $"the rule belongs to the trusted issuer '{rule.Issuer}', not '{issuer.Name}'.";
        }

        if (ValidateAudience(rule.Audience, issuer) is { } audienceError)
        {
            return audienceError;
        }

        if (string.IsNullOrEmpty(rule.Subject))
        {
            return "the subject is required.";
        }

        if (rule.Subject.Length > MaxSubjectLength)
        {
            return $"the subject can have at most {MaxSubjectLength} characters.";
        }

        var hasWildcard = rule.Subject.Contains('*');

        if (hasWildcard && !IsSeparatorAnchoredWildcard(rule.Subject))
        {
            return "the subject can contain * only once, as its last character, right after : or /.";
        }

        if (ValidateClaims(rule.Claims) is { } claimsError)
        {
            return claimsError;
        }

        // Names and slugs can be recycled, ids cannot; immutable ids make a wildcard admit everyone.
        return ClassifyIssuer(issuer.Issuer) switch
        {
            SharedIssuerKind.GitHub when !Pins(rule, issuer, GitHubIdClaims)
                => "a rule of a GitHub Actions issuer must pin repository_owner_id, repository_id or enterprise_id, in its claims or in the issuer's required claims, because names can be recycled and ids cannot.",
            SharedIssuerKind.Vercel when !Pins(rule, issuer, VercelIdClaims)
                => "a rule of a Vercel issuer must pin owner_id, in its claims or in the issuer's required claims, because team names can be recycled and ids cannot.",
            SharedIssuerKind.Google or SharedIssuerKind.Entra when hasWildcard
                => "the subject of a Google or Entra issuer cannot use *: sub is the identity's immutable id, so a wildcard would admit every identity of the tenant or of Google.",
            SharedIssuerKind.None when hasWildcard && !HasAnyPin(rule, issuer)
                => "a subject with * needs at least one claim pin, in the rule's claims or in the issuer's required claims.",
            _ => null,
        };
    }

    private static string? ValidateAudience(string? audience, FederatedIssuer issuer)
    {
        if (string.IsNullOrEmpty(audience))
        {
            return "the audience is required.";
        }

        if (audience.Length > MaxAudienceLength)
        {
            return $"the audience can have at most {MaxAudienceLength} characters.";
        }

        var trimmed = TrimOneTrailingSlash(audience);

        if (ForeignAudiences.Any(foreign => string.Equals(trimmed, foreign, StringComparison.OrdinalIgnoreCase))
            || ForeignAudiencePrefixes.Any(prefix => audience.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return $"the audience {audience} belongs to another service.";
        }

        // Kubernetes puts its issuer into the default --api-audiences, so every pod's default token carries it.
        if (string.Equals(trimmed, TrimOneTrailingSlash(issuer.Issuer), StringComparison.OrdinalIgnoreCase))
        {
            return $"the audience {audience} is the issuer itself, the default audience of its tokens for other services.";
        }

        return null;
    }

    private static bool IsSeparatorAnchoredWildcard(string subject)
    {
        var index = subject.IndexOf('*');
        return index == subject.Length - 1
            && index > 0
            && subject[index - 1] is ':' or '/';
    }

    private static bool Pins(FederationRule rule, FederatedIssuer issuer, string[] claimNames)
    {
        return PinsAny(rule.Claims, claimNames) || PinsAny(issuer.RequiredClaims, claimNames);
    }

    private static bool PinsAny(IReadOnlyDictionary<string, IReadOnlyList<string>>? claims, string[] claimNames)
    {
        return claims is not null
            && claims.Any(pair => pair.Value is { Count: > 0 }
                && claimNames.Any(name => FederationClaimKeys.IsTopLevel(pair.Key, name)));
    }

    private static bool HasAnyPin(FederationRule rule, FederatedIssuer issuer)
    {
        return rule.Claims is { Count: > 0 } || issuer.RequiredClaims is { Count: > 0 };
    }

    private static string TrimOneTrailingSlash(string value)
    {
        return value.EndsWith('/') ? value[..^1] : value;
    }

    // Any '@' before the host, also an empty user info that Uri drops.
    private static bool HasUserInfo(string url, Uri uri)
    {
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return true;
        }

        var authorityStart = url.IndexOf("//", StringComparison.Ordinal);

        if (authorityStart < 0)
        {
            return false;
        }

        authorityStart += 2;
        var authorityEnd = url.IndexOfAny(['/', '?', '#'], authorityStart);
        var authority = authorityEnd < 0 ? url[authorityStart..] : url[authorityStart..authorityEnd];
        return authority.Contains('@');
    }

    // The host of an issuer, also of one without a scheme (Google may issue "accounts.google.com"), in its ASCII form,
    // so look-alike Unicode spellings classify like the real host.
    private static string? GetHost(string? issuer)
    {
        if (string.IsNullOrWhiteSpace(issuer))
        {
            return null;
        }

        if (!TryGetHost(issuer, out var host) && !TryGetHost("https://" + issuer, out host))
        {
            return null;
        }

        return host.TrimEnd('.').ToLowerInvariant();
    }

    private static bool TryGetHost(string value, out string host)
    {
        host = string.Empty;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.HostNameType != UriHostNameType.Dns
            || string.IsNullOrEmpty(uri.IdnHost))
        {
            return false;
        }

        host = uri.IdnHost;
        return true;
    }

    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
