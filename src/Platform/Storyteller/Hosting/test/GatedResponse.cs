namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests;

// A response that waits for the test: Entered completes when the request arrives, and the answer goes out on Release.
// The wait honours the request's cancellation, as a real connection does.
internal sealed class GatedResponse
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<HttpResponseMessage> _response;

    public GatedResponse(Func<HttpResponseMessage> response)
    {
        _response = response;
    }

    public Task Entered => _entered.Task.WaitAsync(HostingFixture.Patience);

    public void Release()
    {
        _release.TrySetResult();
    }

    public async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _entered.TrySetResult();
        await _release.Task.WaitAsync(cancellationToken);
        return _response();
    }
}
