using System.Security.Claims;

using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Accessing;

// Whoever can change a person's email at the identity provider (a WorkOS dashboard member, for example) can then sign
// in as that person: the sub stays, so it is the same account with the same roles. The user name stored when the
// account was registered is the evidence, and nothing refreshes it, so a sign-in under another name is logged. Log
// only: nothing is refused and the stored name stays.
public static class AccountUserNameCheck
{
    // At most this many access points are named in the log of an elevated account.
    public const int MaxLoggedAccessPoints = 20;

    // The claims a user name is taken from at registration, in that order.
    public static IReadOnlyList<string> UserNameClaimTypes { get; } = ["preferred_username", "unique_name", ClaimTypes.Upn];

    // The user name the claims give, as registration reads it: the first of UserNameClaimTypes that is present, trimmed.
    public static string? GetUserName(IEnumerable<Claim> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        var list = claims as IReadOnlyList<Claim> ?? claims.ToList();

        foreach (var claimType in UserNameClaimTypes)
        {
            if (list.FirstOrDefault(claim => claim.Type == claimType) is { } claim)
            {
                return claim.Value.Trim();
            }
        }

        return null;
    }

    // True when it logged a change, as one Warning. No evidence, no log: the claims name nobody, the stored name is
    // blank, or it is the account id (an owner DbCreator seeded before the person signed in). Names are compared
    // ignoring case, as emails and Entra ID names are. An account that is an Administrator or Owner anywhere (its own
    // access map) gets its own message, for an alert.
    public static bool LogIfChanged(ILogger logger, Account account, IEnumerable<Claim> claims, string channel)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(claims);

        var current = GetUserName(claims);

        if (string.IsNullOrEmpty(current) || string.IsNullOrWhiteSpace(account.UserName))
        {
            return false;
        }

        var stored = account.UserName.Trim();

        if (string.Equals(stored, account.Id, StringComparison.Ordinal)
            || string.Equals(current, stored, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var elevated = (account.AccessMap ?? new Dictionary<string, AccountRole>())
            .Where(entry => entry.Value >= AccountRole.Administrator)
            .Select(entry => entry.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

        if (elevated.Count == 0)
        {
            logger.LogWarning(
                "The user name of the account {AccountId} differs from the stored one at {Channel}: {StoredUserName} -> {TokenUserName}",
                account.Id,
                channel,
                stored,
                current);
            return true;
        }

        var points = string.Join(", ", elevated.Take(MaxLoggedAccessPoints));

        if (elevated.Count > MaxLoggedAccessPoints)
        {
            points += $" and {elevated.Count - MaxLoggedAccessPoints} more";
        }

        logger.LogWarning(
            "The user name of the elevated account {AccountId} differs from the stored one at {Channel}: {StoredUserName} -> {TokenUserName} (Administrator or Owner of {ElevatedAccessPoints})",
            account.Id,
            channel,
            stored,
            current,
            points);
        return true;
    }
}
