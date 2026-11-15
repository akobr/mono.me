using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Storyteller.Sdk.Credentials;

// The platform token of a Federated machine from code, for any platform; asked before every exchange, and its token is
// trimmed. A StorytellerAuthenticationException passes as it is; a cancellation the caller did not request, a
// TimeoutException or an HttpRequestException is Unavailable; an empty token or any other exception is Rejected.
public delegate ValueTask<string> StorytellerSubjectTokenProvider(StorytellerSubjectTokenRequest request, CancellationToken cancellationToken);
