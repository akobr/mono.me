namespace _42.Platform.Storyteller.Accessing.Federation;

// The RFC 8693 exchange behind POST v1/{organization}/{project}/access/token: a platform token of a Federated machine
// for a short-lived Storyteller token. It checks the project policy, the trusted issuers and only then the machine,
// so the anonymous endpoint does no work for unknown projects and machines.
public interface IFederatedTokenExchange
{
    // A refused request is a result with the OAuth error, never an exception.
    Task<FederatedTokenExchangeResult> ExchangeAsync(FederatedTokenExchangeRequest request, CancellationToken cancellationToken = default);
}
