using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

namespace _42.Platform.Storyteller.Accessing;

// The outcome of IUserSubjectTokenValidator: the person a subject token names, or why it is refused.
public sealed record UserSubjectTokenResult
{
    private UserSubjectTokenResult()
    {
    }

    [MemberNotNullWhen(true, nameof(AccountId))]
    [MemberNotNullWhen(false, nameof(RefusalReason))]
    public bool IsAccepted => RefusalReason is null;

    // UserIdentity.GetAccountId of the token.
    public string? AccountId { get; private init; }

    // azp, appid or client_id of the token; null when it names no client.
    public string? ClientId { get; private init; }

    // The token's exp, which caps the lifetime of an issued token.
    public DateTimeOffset ExpiresAt { get; private init; }

    public DateTimeOffset? AuthTime { get; private init; }

    // jti, or uti for Entra ID; null when the token has neither.
    public string? TokenId { get; private init; }

    // The validated claims, in the shape the provider's bearer validator gives them.
    public IReadOnlyList<Claim> Claims { get; private init; } = [];

    // For the logs only; it never contains the token.
    public string? RefusalReason { get; private init; }

    public static UserSubjectTokenResult Accepted(
        string accountId,
        string? clientId,
        DateTimeOffset expiresAt,
        DateTimeOffset? authTime,
        string? tokenId,
        IReadOnlyList<Claim> claims)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(claims);

        return new UserSubjectTokenResult
        {
            AccountId = accountId,
            ClientId = clientId,
            ExpiresAt = expiresAt,
            AuthTime = authTime,
            TokenId = tokenId,
            Claims = claims,
        };
    }

    public static UserSubjectTokenResult Refused(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new UserSubjectTokenResult { RefusalReason = reason };
    }

    // A token that fails signature, issuer, audience or lifetime validation. Only the exception type is kept: the
    // messages of the token handlers can quote the token when PII logging is on (Development).
    public static UserSubjectTokenResult NotValid(Exception? exception)
    {
        return Refused($"The token is not valid ({exception?.GetType().Name ?? "no reason given"}).");
    }
}
