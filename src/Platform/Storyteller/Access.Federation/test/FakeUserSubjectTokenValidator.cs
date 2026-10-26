using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller.Access.Federation.UnitTests;

// Answers every subject token with Result (or throws Error) and records what it was asked, for the exchange without a
// provider. The providers' own rules are tested in their projects.
internal sealed class FakeUserSubjectTokenValidator : IUserSubjectTokenValidator
{
    public UserSubjectTokenResult Result { get; set; } = UserSubjectTokenResult.Refused("No result is set.");

    public Exception? Error { get; set; }

    public List<Call> Calls { get; } = [];

    public Task<UserSubjectTokenResult> ValidateAsync(
        string subjectToken,
        IReadOnlyCollection<string> audiences,
        IReadOnlyCollection<string> allowedClientIds,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(new Call(subjectToken, [.. audiences], [.. allowedClientIds]));
        return Error is { } error ? Task.FromException<UserSubjectTokenResult>(error) : Task.FromResult(Result);
    }

    public sealed record Call(string SubjectToken, string[] Audiences, string[] AllowedClientIds);
}
