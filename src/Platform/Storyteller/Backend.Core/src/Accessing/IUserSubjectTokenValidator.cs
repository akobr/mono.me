namespace _42.Platform.Storyteller.Accessing;

// Validates the subject token of a user token exchange: a user token of the deployment's identity provider, checked by
// that provider's rules, and issued for the actor (audience binding). Each provider module registers one.
public interface IUserSubjectTokenValidator
{
    // audiences: the actor's SubjectAudiences, one of which the token's aud must be. allowedClientIds: the actor's
    // AllowedClientIds, one of which the token's client must be when the list is not empty.
    // A refusal carries the reason for the logs. Throws BearerKeyRetrievalException when the signing keys cannot be
    // retrieved.
    Task<UserSubjectTokenResult> ValidateAsync(
        string subjectToken,
        IReadOnlyCollection<string> audiences,
        IReadOnlyCollection<string> allowedClientIds,
        CancellationToken cancellationToken = default);
}
