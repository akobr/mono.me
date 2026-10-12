namespace _42.Platform.Storyteller.KeyValues.Policy;

// The policy of a project, with the defaults when none was stored. Cached briefly.
public interface IKeyValuePolicyStore
{
    Task<KeyValuePolicy> GetAsync(string organization, string project, CancellationToken cancellationToken = default);

    // Replaces the policy; a concurrent change (the stored version differs from policy.Version - 1) fails with PolicyVersionMismatch.
    Task<KeyValuePolicy> SetAsync(string organization, string project, KeyValuePolicy policy, CancellationToken cancellationToken = default);

    // Every project that has a stored policy, for the maintenance runs.
    Task<IReadOnlyList<(string Organization, string Project, KeyValuePolicy Policy)>> ListAsync(CancellationToken cancellationToken = default);
}

public static class KeyValuePolicyDefaults
{
    public static KeyValuePolicy Create() => new();
}
