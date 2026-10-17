namespace _42.Platform.Storyteller.Accessing.Federation;

// The trusted issuers of a project. Callers validate an issuer (FederationValidation) before they store it.
public interface IFederatedIssuerStore
{
    // Every trusted issuer of the project, ordered by name. Cached per instance: a write on another instance
    // shows up within a minute.
    Task<IReadOnlyList<FederatedIssuer>> ListAsync(string organization, string project, CancellationToken cancellationToken = default);

    // Read from the store, not from the list cache.
    Task<FederatedIssuer?> GetAsync(string organization, string project, string name, CancellationToken cancellationToken = default);

    // Creates the issuer named issuer.Name or replaces it. InvalidInputException when it would be the project's
    // issuer number MaxIssuersPerProject + 1 or when it changes the Issuer of an existing one.
    Task<FederatedIssuer> SetAsync(string organization, string project, FederatedIssuer issuer, CancellationToken cancellationToken = default);

    // False when the issuer did not exist.
    Task<bool> DeleteAsync(string organization, string project, string name, CancellationToken cancellationToken = default);
}
