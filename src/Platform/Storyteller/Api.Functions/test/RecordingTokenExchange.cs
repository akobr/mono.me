using _42.Platform.Storyteller.Accessing.Federation;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

internal sealed class RecordingTokenExchange : IFederatedTokenExchange
{
    public List<FederatedTokenExchangeRequest> Requests { get; } = [];

    public FederatedTokenExchangeResult Result { get; set; } =
        FederatedTokenExchangeResult.Success("storyteller-token", OAuthTokenExchange.TokenTypes.AccessToken, 900, "Default.Read Annotation.Read");

    public Task<FederatedTokenExchangeResult> ExchangeAsync(FederatedTokenExchangeRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(Result);
    }
}
