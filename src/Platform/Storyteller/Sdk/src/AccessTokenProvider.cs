using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Kiota.Abstractions.Authentication;

namespace ApiSdk;

public class AccessTokenProvider(ITokenService tokenService) : IAccessTokenProvider
{
    // Called by Kiota before each request
    public async Task<string> GetAuthorizationTokenAsync(
        Uri uri,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        // You control how the token is fetched / cached
        return await tokenService.GetAccessTokenAsync(cancellationToken);
    }

    // Optional but recommended: restrict where tokens are sent
    public AllowedHostsValidator AllowedHostsValidator { get; } =
        new AllowedHostsValidator(["api.yourdomain.com"]);
}
