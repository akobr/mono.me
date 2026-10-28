namespace _42.Platform.Storyteller;

// What a token must satisfy besides its signature and issuer. Audiences: its aud must contain one of them;
// RequireSingleAudience: and no other. MaxLifetimeSeconds: the longest exp - iat, or without iat (which RequireIssuedAt
// refuses) the longest exp - now, less the skew; null: no limit besides exp. MaxAgeSeconds: the longest now - iat, less
// the skew, whatever exp says; a token without iat passes it unless RequireIssuedAt refuses it.
internal sealed record IssuerTokenPolicy(
    IReadOnlyList<string> Audiences,
    TimeSpan ClockSkew,
    bool RequireSingleAudience = false,
    bool RequireIssuedAt = false,
    int? MaxLifetimeSeconds = null,
    int? MaxAgeSeconds = null);
