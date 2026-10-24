using System.Collections.Concurrent;

using _42.Platform.Storyteller.Mcp.Authentication;

using Microsoft.IdentityModel.JsonWebTokens;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Support;

// On-behalf-of without Entra: the downstream token names the person (downstream:{sub}), so a test sees whose token the
// API got. Failure replaces the exchange with an exception.
internal sealed class StubTokenProvider : IStorytellerTokenProvider
{
    public ConcurrentQueue<string> UserTokens { get; } = new();

    public Exception? Failure { get; set; }

    public static string DownstreamFor(string subject) => $"downstream:{subject}";

    public Task<string> GetTokenAsync(string userToken, CancellationToken cancellationToken)
    {
        UserTokens.Enqueue(userToken);

        if (Failure is not null)
        {
            throw Failure;
        }

        var subject = new JsonWebToken(userToken).Subject;
        return Task.FromResult(DownstreamFor(subject));
    }
}
