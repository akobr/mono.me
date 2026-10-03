using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller;

// Fills email and name from WorkOS when the access token has no JWT template for them.
// Used only on account registration, so the lookup is not cached.
public sealed class AuthKitUserProfileResolver : IUserProfileResolver
{
    private readonly WorkOsManagementClient _client;

    public AuthKitUserProfileResolver(WorkOsManagementClient client)
    {
        _client = client;
    }

    public async Task<UserProfile?> ResolveAsync(string subject, CancellationToken cancellationToken = default)
    {
        // Without the management key there is nothing to ask; the caller reports the missing claim.
        if (!_client.IsConfigured || string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        var user = await _client.GetUserAsync(subject, cancellationToken);

        if (user is null)
        {
            return null;
        }

        var email = string.IsNullOrWhiteSpace(user.Email) ? null : user.Email.Trim();
        var name = AuthKitClaimNormalizer.JoinNames(user.FirstName, user.LastName) ?? email;
        return new UserProfile(email, name);
    }
}
