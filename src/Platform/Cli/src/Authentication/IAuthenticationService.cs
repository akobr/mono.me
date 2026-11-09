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
    // organizationId (-o|--organization) is the id of the organization the session must belong to, and wins over
    // selectOrganization: a user who is not a member fails with AuthenticationFailureReason.Configuration, and no
    // session is stored. Only AuthKit has organizations; the other providers refuse it.
    Task<SignedInUser> LoginWithDeviceCodeAsync(
        Func<DeviceCodePrompt, Task> onPrompt,
        Func<IReadOnlyList<OrganizationChoice>, Task<string>>? selectOrganization = null,
        string? organizationId = null,
        CancellationToken cancellationToken = default);

    Task LogoutAsync(CancellationToken cancellationToken = default);
}
