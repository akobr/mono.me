using System.Collections.Concurrent;

using _42.Platform.Storyteller.Mcp.Authentication;

using Microsoft.IdentityModel.JsonWebTokens;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Support;

// The token exchange without the API: the Storyteller token names the person (downstream:{sub}), so a test sees whose
// token the API got. Restrictions are those of the actor (none by default); Failure replaces the exchange with an
// exception.
internal sealed class StubTokenProvider : IStorytellerTokenProvider
{
    public ConcurrentQueue<string> UserTokens { get; } = new();

    public Exception? Failure { get; set; }

    public IReadOnlySet<string> Restrictions { get; set; } = new HashSet<string>(StringComparer.Ordinal);

    public static string DownstreamFor(string subject) => $"downstream:{subject}";

    public Task<StorytellerToken> GetTokenAsync(string userToken, CancellationToken cancellationToken)
    {
        UserTokens.Enqueue(userToken);

        if (Failure is not null)
        {
            throw Failure;
        }

        var subject = new JsonWebToken(userToken).Subject;
        return Task.FromResult(new StorytellerToken(DownstreamFor(subject), DateTimeOffset.UtcNow.AddMinutes(10), "User.Impersonation", Restrictions));
    }
}
