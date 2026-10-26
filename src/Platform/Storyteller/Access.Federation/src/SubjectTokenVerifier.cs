using System.Text.Json;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

    private readonly IssuerTokenVerifier _tokenVerifier;
    private readonly IOptions<FederationOptions> _options;
    private readonly ILogger<SubjectTokenVerifier> _logger;

    public SubjectTokenVerifier(
        FederatedIssuerKeyResolver keyResolver,
        IOptions<FederationOptions> options,
        TimeProvider timeProvider,
        ILogger<SubjectTokenVerifier> logger)
    {
        _tokenVerifier = new IssuerTokenVerifier(keyResolver, timeProvider);
        _options = options;
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

        // Storyteller's own tokens never become machine tokens, even through a trusted issuer stored before that was refused.
        if (FederationValidation.IsOwnIssuer(token.Issuer, options))
        {
            LogRefusal(machine, null, "the token is a Storyteller token");
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
        var audiences = group.Select(candidate => candidate.Rule.Audience).Distinct(StringComparer.Ordinal).ToList();

        // A platform token has one aud, an iat and a short life: the issuer's maximum, else an hour.
        var policy = new IssuerTokenPolicy(
            audiences,
            TimeSpan.FromSeconds(options.ClockSkewSeconds),
            RequireSingleAudience: true,
            RequireIssuedAt: true,
            MaxLifetimeSeconds: issuer.MaxTokenLifetimeSeconds ?? FederationValidation.DefaultMaxTokenLifetimeSeconds);
        var verification = await _tokenVerifier.VerifyAsync(subjectToken, token, organization, project, issuer, policy, cancellationToken);

        if (!verification.IsValid)
        {
            LogRefusal(machine, issuer, verification.RefusalReason);
            return verification.SignatureVerified ? GroupOutcome.VerifiedWithoutMatch : GroupOutcome.Refused;
        }

        if (IssuerTokenVerifier.GetString(token.Payload, "sub") is not { } subject)
        {
            LogRefusal(machine, issuer, "the token has no sub");
            return GroupOutcome.VerifiedWithoutMatch;
        }

        var audience = IssuerTokenVerifier.GetSingleAudience(token.Payload)!;

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
