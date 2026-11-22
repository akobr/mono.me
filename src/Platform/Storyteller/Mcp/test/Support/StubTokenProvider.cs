using System.Collections.Concurrent;

using _42.Platform.Storyteller.Mcp.Authentication;

using Microsoft.IdentityModel.JsonWebTokens;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Support;

// The token exchange without the API: the Storyteller token names the person (downstream:{sub}), so a test sees whose
// token the API got. Restrictions are those of the actor (none by default); Failure replaces the exchange with an
// exception; Pending, while set, holds every exchange until it completes or the caller stops waiting. Forgotten records
// the calls of Forget.
internal sealed class StubTokenProvider : IStorytellerTokenProvider
{
    public ConcurrentQueue<string> UserTokens { get; } = new();

    public ConcurrentQueue<(string UserToken, string AccessToken)> Forgotten { get; } = new();

    public Exception? Failure { get; set; }

    public TaskCompletionSource? Pending { get; set; }

    public IReadOnlySet<string> Restrictions { get; set; } = new HashSet<string>(StringComparer.Ordinal);

    public static string DownstreamFor(string subject) => $"downstream:{subject}";

    public async Task<StorytellerToken> GetTokenAsync(string userToken, CancellationToken cancellationToken)
    {
        UserTokens.Enqueue(userToken);

        if (Pending is { } pending)
        {
            await pending.Task.WaitAsync(cancellationToken);
        }

        if (Failure is not null)
        {
            throw Failure;
        }

        var subject = new JsonWebToken(userToken).Subject;
        return new StorytellerToken(DownstreamFor(subject), DateTimeOffset.UtcNow.AddMinutes(10), "User.Impersonation", Restrictions);
    }

    public void Forget(string userToken, string accessToken)
    {
        Forgotten.Enqueue((userToken, accessToken));
    }
}
