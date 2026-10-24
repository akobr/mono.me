using System;
using System.Net.Http.Headers;

namespace _42.Platform.Storyteller.Sdk.Credentials;

// IssuedAt is when the token arrived, for the renewal of a token that lives less than two minutes.
internal sealed record AccessToken(string Value, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt)
{
    public AuthenticationHeaderValue Header { get; } = new("Bearer", Value);

    // A record prints its members; the token must never reach a log.
    public override string ToString()
    {
        return $"{nameof(AccessToken)} {{ ExpiresAt = {ExpiresAt:O} }}";
    }
}
