using System.Diagnostics.CodeAnalysis;

namespace _42.Platform.Storyteller;

// SignatureVerified: the issuer's key verified the token, even when the policy refused it afterwards. RefusalReason is
// for the logs; it never contains the token.
internal sealed record IssuerTokenVerification(bool SignatureVerified, string? RefusalReason)
{
    public static readonly IssuerTokenVerification Valid = new(true, null);

    [MemberNotNullWhen(false, nameof(RefusalReason))]
    public bool IsValid => RefusalReason is null;

    public static IssuerTokenVerification NotVerified(string reason) => new(false, reason);

    public static IssuerTokenVerification Refused(string reason) => new(true, reason);
}
