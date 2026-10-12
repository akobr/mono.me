using System.Collections.Concurrent;
using _42.Platform.Storyteller.KeyValues.Policy;

namespace _42.Platform.Storyteller.KeyValues.Azure.KeyVault.IntegrationTests;

internal sealed class InMemoryPolicyStore : IKeyValuePolicyStore
{
    private readonly ConcurrentDictionary<string, KeyValuePolicy> _policies = new(StringComparer.Ordinal);

    public Task<KeyValuePolicy> GetAsync(string organization, string project, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_policies.GetValueOrDefault($"{organization}.{project}") ?? new KeyValuePolicy());
    }

    public Task<KeyValuePolicy> SetAsync(string organization, string project, KeyValuePolicy policy, CancellationToken cancellationToken = default)
    {
        _policies[$"{organization}.{project}"] = policy;
        return Task.FromResult(policy);
    }

    public Task<IReadOnlyList<(string Organization, string Project, KeyValuePolicy Policy)>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<(string, string, KeyValuePolicy)> list = _policies
            .Select(entry => (entry.Key.Split('.', 2)[0], entry.Key.Split('.', 2)[1], entry.Value))
            .ToList();
        return Task.FromResult(list);
    }
}
