using System.Collections.Concurrent;
using _42.Platform.Storyteller.Accessing.Federation;

namespace _42.Platform.Storyteller;

// Trusted issuers of one instance, the default when Cosmos DB is not registered (tests, local runs).
// Behaves like CosmosFederatedIssuerStore: ordered by name, at most MaxIssuersPerProject per project, Issuer immutable.
public class InMemoryFederatedIssuerStore : IFederatedIssuerStore
{
    private readonly ConcurrentDictionary<string, SortedDictionary<string, FederatedIssuer>> _projects = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<FederatedIssuer>> ListAsync(string organization, string project, CancellationToken cancellationToken = default)
    {
        if (!_projects.TryGetValue(GetProjectKey(organization, project), out var issuers))
        {
            return Task.FromResult<IReadOnlyList<FederatedIssuer>>([]);
        }

        lock (issuers)
        {
            return Task.FromResult<IReadOnlyList<FederatedIssuer>>(issuers.Values.ToList());
        }
    }

    public Task<FederatedIssuer?> GetAsync(string organization, string project, string name, CancellationToken cancellationToken = default)
    {
        if (!FederationValidation.IsValidName(name)
            || !_projects.TryGetValue(GetProjectKey(organization, project), out var issuers))
        {
            return Task.FromResult<FederatedIssuer?>(null);
        }

        lock (issuers)
        {
            return Task.FromResult(issuers.GetValueOrDefault(name));
        }
    }

    public Task<FederatedIssuer> SetAsync(string organization, string project, FederatedIssuer issuer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(issuer);

        if (!FederationValidation.IsValidName(issuer.Name))
        {
            throw new InvalidInputException($"The name '{issuer.Name}' of a trusted issuer is not valid.", ErrorCodes.InvalidFederation);
        }

        var issuers = _projects.GetOrAdd(GetProjectKey(organization, project), _ => new SortedDictionary<string, FederatedIssuer>(StringComparer.Ordinal));

        lock (issuers)
        {
            if (FederationValidation.ValidateIssuerChange(issuer, issuers.Values) is { } error)
            {
                throw new InvalidInputException(error, ErrorCodes.InvalidFederation);
            }

            issuers[issuer.Name] = issuer;
        }

        return Task.FromResult(issuer);
    }

    public Task<bool> DeleteAsync(string organization, string project, string name, CancellationToken cancellationToken = default)
    {
        if (!_projects.TryGetValue(GetProjectKey(organization, project), out var issuers))
        {
            return Task.FromResult(false);
        }

        lock (issuers)
        {
            return Task.FromResult(issuers.Remove(name));
        }
    }

    private static string GetProjectKey(string organization, string project) => $"{organization}.{project}";
}
