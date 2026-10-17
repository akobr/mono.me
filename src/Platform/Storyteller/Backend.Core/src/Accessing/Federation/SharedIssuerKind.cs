namespace _42.Platform.Storyteller.Accessing.Federation;

// Well-known issuers whose tokens need extra rules, detected by the host of FederatedIssuer.Issuer.
public enum SharedIssuerKind
{
    None = 0,

    // token.actions.githubusercontent.com (also enterprise paths) and token.actions.<sub>.ghe.com: `sub` is built from
    // owner and repository names, which can be recycled, so a rule pins an id.
    GitHub,

    // oidc.vercel.com (global and team issuers): `sub` is built from team and project slugs, so a rule pins owner_id.
    Vercel,

    // accounts.google.com, with or without a scheme: `sub` is the immutable id of the identity, never a wildcard.
    Google,

    // login.microsoftonline.com and sts.windows.net: `sub` is the immutable object id, never a wildcard.
    Entra,
}
