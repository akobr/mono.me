using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Cli.Authentication;

public interface IAuthenticationService
{
    // Silent. Null means a sign-in is needed.
    Task<SignedInUser?> GetSignedInUserAsync(CancellationToken cancellationToken = default);

    // Silent, refreshes when needed. Null means a sign-in is needed.
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    // Throws AuthenticationException when the sign-in does not complete.
    // selectOrganization is asked only when an AuthKit user belongs to several organizations; null picks the first.
    Task<SignedInUser> LoginWithDeviceCodeAsync(
        Func<DeviceCodePrompt, Task> onPrompt,
        Func<IReadOnlyList<OrganizationChoice>, Task<string>>? selectOrganization = null,
        CancellationToken cancellationToken = default);

    Task LogoutAsync(CancellationToken cancellationToken = default);
}
