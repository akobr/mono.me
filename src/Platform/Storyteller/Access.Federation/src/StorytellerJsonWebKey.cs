namespace _42.Platform.Storyteller;

// A public signing key of Storyteller tokens as an RFC 7517 JSON Web Key, for the JWKS: kty RSA with the base64url
// modulus n and exponent e, alg RS256 and use sig. Never a private part.
public sealed record StorytellerJsonWebKey(string Kid, string Kty, string N, string E, string Alg, string Use);
