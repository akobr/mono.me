using System.Text.Json;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller;

// Verifies a platform token against the federation rules of a machine. Every refusal is one null result for the caller;
// the reason is logged with the machine id and the trusted issuer name, never with the token.
public class SubjectTokenVerifier
{
    // Asymmetric signatures only: never `none`, and no HS* that a public key could be abused as the secret of.
    public static readonly IReadOnlyList<string> AllowedAlgorithms =
    [
        SecurityAlgorithms.RsaSha256,
        SecurityAlgorithms.RsaSha384,
        SecurityAlgorithms.RsaSha512,
        SecurityAlgorithms.RsaSsaPssSha256,
        SecurityAlgorithms.RsaSsaPssSha384,
        SecurityAlgorithms.RsaSsaPssSha512,
        SecurityAlgorithms.EcdsaSha256,
        SecurityAlgorithms.EcdsaSha384,
        SecurityAlgorithms.EcdsaSha512,
    ];

    private readonly FederatedIssuerKeyResolver _keyResolver;
    private readonly IOptions<FederationOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SubjectTokenVerifier> _logger;
    private readonly JsonWebTokenHandler _handler = new() { MapInboundClaims = false };

    public SubjectTokenVerifier(
        FederatedIssuerKeyResolver keyResolver,
        IOptions<FederationOptions> options,
        TimeProvider timeProvider,
        ILogger<SubjectTokenVerifier> logger)
    {
        _keyResolver = keyResolver;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    // The first rule of the machine the token matches, or null when it is refused. Throws BearerKeyRetrievalException
    // when the keys of a trusted issuer cannot be retrieved and no other issuer verified the token.
    public async Task<SubjectTokenMatch?> VerifyAsync(
        string subjectToken,
        MachineAccess machine,
        IReadOnlyCollection<FederatedIssuer> issuers,
        string organization,
        string project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(issuers);

        var options = _options.Value;

        // 1. Size and form, before anything is parsed further.
        if (!UnverifiedJwt.TryRead(subjectToken, options.MaxSubjectTokenBytes, out var token, out var formError))
        {
            LogRefusal(machine, null, formError);
            return null;
        }

        // 2. Only rules whose trusted issuer has exactly the token's `iss`; without one nothing is fetched.
        var candidates = (machine.FederationRules ?? [])
            .Select((rule, index) => new Candidate(
                rule,
                index + 1,
                issuers.FirstOrDefault(issuer => rule is not null && string.Equals(issuer.Name, rule.Issuer, StringComparison.Ordinal))))
            .Where(candidate => candidate.Issuer is not null && string.Equals(candidate.Issuer.Issuer, token.Issuer, StringComparison.Ordinal))
            .ToList();

        if (candidates.Count < 1)
        {
            _logger.LogDebug(
                "A subject token for the machine {MachineId} is refused: its issuer is not trusted by any of the machine's rules",
                machine.Id);
            return null;
        }

        if (string.IsNullOrEmpty(token.KeyId))
        {
            LogRefusal(machine, candidates[0].Issuer, "the token has no kid");
            return null;
        }

        BearerKeyRetrievalException? unavailable = null;
        var signatureVerified = false;

        // 3. Per trusted issuer, in rule order: that issuer's keys and only its rules' audiences, so a rule never matches
        // a token another trusted issuer verified, even one with the same Issuer string.
        foreach (var group in candidates.GroupBy(candidate => candidate.Issuer!.Name, StringComparer.Ordinal))
        {
            GroupOutcome outcome;

            try
            {
                outcome = await VerifyGroupAsync(subjectToken, token, group.ToList(), machine, organization, project, options, cancellationToken);
            }
            catch (BearerKeyRetrievalException exception)
            {
                unavailable ??= exception;
                continue;
            }

            if (outcome.Match is not null)
            {
                return outcome.Match;
            }

            signatureVerified |= outcome.SignatureVerified;
        }

        // Unavailable keys answer 503 only when no other trusted issuer could verify the token.
        if (unavailable is not null && !signatureVerified)
        {
            throw unavailable;
        }

        // 4. No match.
        return null;
    }

    private async Task<GroupOutcome> VerifyGroupAsync(
        string subjectToken,
        UnverifiedJwt token,
        IReadOnlyList<Candidate> group,
        MachineAccess machine,
        string organization,
        string project,
        FederationOptions options,
        CancellationToken cancellationToken)
    {
        var issuer = group[0].Issuer!;
        var keys = await _keyResolver.GetKeysAsync(organization, project, issuer, cancellationToken);

        if (keys is null)
        {
            LogRefusal(machine, issuer, "the trusted issuer is misconfigured");
            return GroupOutcome.Refused;
        }

        var audiences = group.Select(candidate => candidate.Rule.Audience).Distinct(StringComparer.Ordinal).ToList();
        var result = await ValidateAsync(subjectToken, issuer, keys, audiences, options);

        if (result.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            var refreshed = await _keyResolver.RefreshForUnknownKeyAsync(organization, project, issuer, keys, cancellationToken);

            if (refreshed is null)
            {
                LogRefusal(machine, issuer, "the token's kid is not in the issuer's key set");
                return GroupOutcome.Refused;
            }

            result = await ValidateAsync(subjectToken, issuer, refreshed, audiences, options);

            // Just fetched, so the issuer may still be publishing the key: retry later.
            if (result.Exception is SecurityTokenSignatureKeyNotFoundException)
            {
                throw new BearerKeyRetrievalException($"The trusted issuer {issuer.Name} did not publish the key {token.KeyId}.");
            }
        }

        if (!result.IsValid)
        {
            LogRefusal(machine, issuer, $"the token did not validate ({result.Exception?.GetType().Name})");
            return GroupOutcome.Refused;
        }

        if (CheckVerifiedToken(token.Payload, issuer, options) is { } tokenError)
        {
            LogRefusal(machine, issuer, tokenError);
            return GroupOutcome.VerifiedWithoutMatch;
        }

        var audience = GetSingleAudience(token.Payload)!;
        var subject = token.Payload.GetProperty("sub").GetString()!;

        foreach (var candidate in group)
        {
            // Re-checked against the current issuer: a rule an issuer change made unsafe stops matching.
            if (FederationValidation.ValidateRule(candidate.Rule, issuer) is { } ruleError)
            {
                LogRefusal(machine, issuer, $"rule {candidate.Number} is skipped: {ruleError}");
                continue;
            }

            if (FindRuleMismatch(candidate.Rule, issuer, token.Payload, audience, subject) is { } mismatch)
            {
                LogRefusal(machine, issuer, $"rule {candidate.Number} mismatched on {mismatch}");
                continue;
            }

            return new GroupOutcome(new SubjectTokenMatch(issuer, candidate.Rule, candidate.Number, subject), SignatureVerified: true);
        }

        return GroupOutcome.VerifiedWithoutMatch;
    }

    private async Task<TokenValidationResult> ValidateAsync(
        string subjectToken,
        FederatedIssuer issuer,
        FederatedIssuerKeys keys,
        IReadOnlyList<string> audiences,
        FederationOptions options)
    {
        var clockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds);

        return await _handler.ValidateTokenAsync(subjectToken, new TokenValidationParameters
        {
            IssuerSigningKeys = keys.Keys,
            TryAllIssuerSigningKeys = false,
            RequireSignedTokens = true,
            ValidAlgorithms = AllowedAlgorithms,
            ValidateIssuer = true,
            IssuerValidator = (tokenIssuer, _, _) => string.Equals(tokenIssuer, issuer.Issuer, StringComparison.Ordinal)
                ? tokenIssuer
                : throw new SecurityTokenInvalidIssuerException("The issuer is not the trusted issuer."),
            ValidateAudience = true,
            RequireAudience = true,
            ValidAudiences = audiences,
            IgnoreTrailingSlashWhenValidatingAudience = false,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = clockSkew,
            LifetimeValidator = TokenLifetime.CreateValidator(_timeProvider, clockSkew),
        });
    }

    // The checks the handler doesn't make, on the verified payload. Null when they pass, else why not.
    private string? CheckVerifiedToken(JsonElement payload, FederatedIssuer issuer, FederationOptions options)
    {
        // A token minted for several relying parties may be replayed by any of them.
        if (GetSingleAudience(payload) is null)
        {
            return "the token has not exactly one audience";
        }

        if (!payload.TryGetProperty("iat", out var issuedAtValue) || !issuedAtValue.TryGetDouble(out var issuedAt))
        {
            return "the token has no iat";
        }

        if (!payload.TryGetProperty("exp", out var expiresValue) || !expiresValue.TryGetDouble(out var expires))
        {
            return "the token has no exp";
        }

        var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();

        if (issuedAt > now + options.ClockSkewSeconds)
        {
            return "the token was issued in the future";
        }

        var maxLifetime = issuer.MaxTokenLifetimeSeconds ?? FederationValidation.DefaultMaxTokenLifetimeSeconds;

        if (expires - issuedAt > maxLifetime)
        {
            return $"the token lives longer than {maxLifetime} seconds";
        }

        if (!payload.TryGetProperty("sub", out var subject)
            || subject.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(subject.GetString()))
        {
            return "the token has no sub";
        }

        return null;
    }

    private static string? FindRuleMismatch(FederationRule rule, FederatedIssuer issuer, JsonElement payload, string audience, string subject)
    {
        if (!string.Equals(rule.Audience, audience, StringComparison.Ordinal))
        {
            return "aud";
        }

        if (!FederationClaimMatcher.SubjectMatches(rule.Subject, subject))
        {
            return "sub";
        }

        if (FederationClaimMatcher.FindMismatch(payload, rule.Claims) is { } claim)
        {
            return $"the claim {claim}";
        }

        return FederationClaimMatcher.FindMismatch(payload, issuer.RequiredClaims) is { } required
            ? $"the required claim {required} of the issuer"
            : null;
    }

    // A string or a one-element array of a string.
    private static string? GetSingleAudience(JsonElement payload)
    {
        if (!payload.TryGetProperty("aud", out var audience))
        {
            return null;
        }

        if (audience.ValueKind == JsonValueKind.Array && audience.GetArrayLength() == 1)
        {
            audience = audience[0];
        }

        return audience.ValueKind == JsonValueKind.String ? audience.GetString() : null;
    }

    private void LogRefusal(MachineAccess machine, FederatedIssuer? issuer, string reason)
    {
        _logger.LogInformation(
            "A subject token for the machine {MachineId} is refused by the trusted issuer {IssuerName}: {Reason}",
            machine.Id,
            issuer?.Name ?? "(none)",
            reason);
    }

    private sealed record Candidate(FederationRule Rule, int Number, FederatedIssuer? Issuer);

    private sealed record GroupOutcome(SubjectTokenMatch? Match, bool SignatureVerified)
    {
        public static readonly GroupOutcome Refused = new(null, false);

        public static readonly GroupOutcome VerifiedWithoutMatch = new(null, true);
    }
}
