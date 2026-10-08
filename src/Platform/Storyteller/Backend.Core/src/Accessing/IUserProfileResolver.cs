namespace _42.Platform.Storyteller.Accessing;

// Looks up profile fields the access token did not carry. Called only when an account is registered.
public interface IUserProfileResolver
{
    // Null when the provider has no profile for the subject or no lookup is configured.
    Task<UserProfile?> ResolveAsync(string subject, CancellationToken cancellationToken = default);
}
