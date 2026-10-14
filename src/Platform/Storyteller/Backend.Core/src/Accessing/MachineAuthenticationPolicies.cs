namespace _42.Platform.Storyteller.Accessing;

// A project enables a set of machine credential kinds; each machine uses one of them.
// A policy stored before that (only CredentialKind) enables just its one kind.
public static class MachineAuthenticationPolicies
{
    // The policy a project applies, with every property filled: the enabled kinds (distinct, in enum order),
    // an enabled default kind and the default certificate lifetime within the deployment's bounds.
    public static MachineAuthenticationPolicy Resolve(MachineAuthenticationPolicy? stored, MachineAuthenticationOptions options)
    {
        var requested = GetRequestedKinds(stored);
        var enabled = Order(requested.Where(Enum.IsDefined));

        if (enabled.Count < 1)
        {
            enabled = [options.DefaultCredentialKind];
        }

        var defaultKind = stored?.CredentialKind is { } kind && enabled.Contains(kind)
            ? kind
            : requested.FirstOrDefault(enabled.Contains, enabled[0]);

        return new MachineAuthenticationPolicy
        {
            EnabledCredentialKinds = enabled,
            CredentialKind = defaultKind,
            CertificateLifetimeDays = Math.Clamp(
                stored?.CertificateLifetimeDays ?? options.CertificateLifetimeDays,
                options.MinCertificateLifetimeDays,
                options.MaxCertificateLifetimeDays),
        };
    }

    public static async Task<MachineAuthenticationPolicy> GetEffectiveAsync(
        this IMachineAuthenticationPolicyStore store,
        string organization,
        string project,
        MachineAuthenticationOptions options)
    {
        var stored = await store.GetAsync(organization, project);
        return Resolve(stored, options);
    }

    // Null when the policy can be stored, else why not.
    public static string? Validate(MachineAuthenticationPolicy? requested, MachineAuthenticationOptions options)
    {
        if (requested is null)
        {
            return "The machine authentication policy is missing.";
        }

        var kinds = GetRequestedKinds(requested);

        if (kinds.Count < 1)
        {
            return "Enable at least one machine credential kind.";
        }

        var unknown = kinds.Where(kind => !Enum.IsDefined(kind)).ToList();

        if (unknown.Count > 0)
        {
            return $"The machine credential kind {unknown[0]:D} doesn't exist.";
        }

        if (requested.CredentialKind is { } defaultKind && !kinds.Contains(defaultKind))
        {
            return $"The default credential kind {defaultKind} is not one of the enabled kinds.";
        }

        if (requested.CertificateLifetimeDays is { } days
            && (days < options.MinCertificateLifetimeDays || days > options.MaxCertificateLifetimeDays))
        {
            return $"The certificate lifetime must be between {options.MinCertificateLifetimeDays} and {options.MaxCertificateLifetimeDays} days.";
        }

        return null;
    }

    // A valid requested policy as it is stored: the kinds distinct and in enum order, the default filled in.
    // The lifetime stays null when not given, so the project follows the deployment default.
    public static MachineAuthenticationPolicy Normalize(MachineAuthenticationPolicy requested)
    {
        var kinds = GetRequestedKinds(requested);

        return requested with
        {
            EnabledCredentialKinds = Order(kinds),
            CredentialKind = requested.CredentialKind ?? kinds[0],
        };
    }

    public static bool IsEnabled(this MachineAuthenticationPolicy policy, MachineCredentialKind kind)
    {
        return GetRequestedKinds(policy).Contains(kind);
    }

    public static bool IssuesCertificate(this MachineCredentialKind kind)
    {
        return kind is MachineCredentialKind.Certificate or MachineCredentialKind.CertificateAndApiKey;
    }

    // Whether the presented credentials are what a machine of this kind has to send.
    // ClientCredentials machines send identity provider tokens, never an API key or a certificate.
    public static bool IsSatisfiedBy(this MachineCredentialKind kind, bool hasCertificate, bool hasApiKey)
    {
        return kind switch
        {
            MachineCredentialKind.ApiKey => hasApiKey,
            MachineCredentialKind.Certificate => hasCertificate,
            MachineCredentialKind.CertificateAndApiKey => hasCertificate && hasApiKey,
            _ => false,
        };
    }

    private static IReadOnlyList<MachineCredentialKind> GetRequestedKinds(MachineAuthenticationPolicy? policy)
    {
        if (policy?.EnabledCredentialKinds is { Count: > 0 } kinds)
        {
            return kinds.Distinct().ToList();
        }

        return policy?.CredentialKind is { } kind ? [kind] : [];
    }

    private static List<MachineCredentialKind> Order(IEnumerable<MachineCredentialKind> kinds)
    {
        return kinds.Distinct().Order().ToList();
    }
}
