using System.Net.Http.Headers;
using _42.Platform.Storyteller.Sdk.Credentials;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Engine;

// A credential that works until a test makes it fail with a chosen reason.
internal sealed class ToggleCredential : StorytellerCredential
{
    private volatile object? _failure;

    public StorytellerAuthenticationFailureReason? Failure
    {
        get => (StorytellerAuthenticationFailureReason?)_failure;
        set => _failure = value;
    }

    public override ValueTask<AuthenticationHeaderValue?> GetAuthorizationAsync(AuthenticationHeaderValue? rejected, CancellationToken cancellationToken)
    {
        return Failure is { } reason
            ? throw new StorytellerAuthenticationException(reason, $"The test credential fails with {reason}.")
            : ValueTask.FromResult<AuthenticationHeaderValue?>(new AuthenticationHeaderValue("Bearer", "token"));
    }
}
