using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

public class MachineAuthenticationPoliciesTests
{
    private readonly MachineAuthenticationOptions _options = new()
    {
        DefaultCredentialKind = MachineCredentialKind.ApiKey,
        CertificateLifetimeDays = 365,
        MinCertificateLifetimeDays = 1,
        MaxCertificateLifetimeDays = 3650,
    };

    [Fact]
    public void Resolve_WithoutAStoredPolicy_EnablesTheDeploymentDefault()
    {
        var policy = MachineAuthenticationPolicies.Resolve(null, _options);

        policy.EnabledCredentialKinds.ShouldBe([MachineCredentialKind.ApiKey]);
        policy.CredentialKind.ShouldBe(MachineCredentialKind.ApiKey);
        policy.CertificateLifetimeDays.ShouldBe(365);
    }

    [Fact]
    public void Resolve_LegacyPolicy_EnablesOnlyItsKind()
    {
        var stored = new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.Certificate, CertificateLifetimeDays = 90 };

        var policy = MachineAuthenticationPolicies.Resolve(stored, _options);

        policy.EnabledCredentialKinds.ShouldBe([MachineCredentialKind.Certificate]);
        policy.CredentialKind.ShouldBe(MachineCredentialKind.Certificate);
        policy.CertificateLifetimeDays.ShouldBe(90);
    }

    [Fact]
    public void Resolve_EnabledKinds_AreDistinctInEnumOrderAndTheFirstListedIsTheDefault()
    {
        var stored = new MachineAuthenticationPolicy
        {
            EnabledCredentialKinds = [MachineCredentialKind.ClientCredentials, MachineCredentialKind.ApiKey, MachineCredentialKind.ClientCredentials],
        };

        var policy = MachineAuthenticationPolicies.Resolve(stored, _options);

        policy.EnabledCredentialKinds.ShouldBe([MachineCredentialKind.ApiKey, MachineCredentialKind.ClientCredentials]);
        policy.CredentialKind.ShouldBe(MachineCredentialKind.ClientCredentials);
    }

    [Fact]
    public void Resolve_ADefaultThatIsNotEnabled_FallsBackToTheFirstEnabledKind()
    {
        var stored = new MachineAuthenticationPolicy
        {
            EnabledCredentialKinds = [MachineCredentialKind.Certificate],
            CredentialKind = MachineCredentialKind.ApiKey,
        };

        MachineAuthenticationPolicies.Resolve(stored, _options).CredentialKind.ShouldBe(MachineCredentialKind.Certificate);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(5000, 3650)]
    public void Resolve_ClampsTheLifetimeToTheDeploymentBounds(int stored, int expected)
    {
        var policy = MachineAuthenticationPolicies.Resolve(new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.Certificate, CertificateLifetimeDays = stored }, _options);

        policy.CertificateLifetimeDays.ShouldBe(expected);
    }

    [Fact]
    public void Resolve_UnknownKindsAreDropped()
    {
        var stored = new MachineAuthenticationPolicy { EnabledCredentialKinds = [(MachineCredentialKind)42] };

        var policy = MachineAuthenticationPolicies.Resolve(stored, _options);

        policy.EnabledCredentialKinds.ShouldBe([MachineCredentialKind.ApiKey]);
    }

    [Fact]
    public void Validate_AcceptsAPolicyWithEnabledKindsADefaultAndALifetime()
    {
        var policy = new MachineAuthenticationPolicy
        {
            EnabledCredentialKinds = [MachineCredentialKind.ApiKey, MachineCredentialKind.Certificate],
            CredentialKind = MachineCredentialKind.Certificate,
            CertificateLifetimeDays = 90,
        };

        MachineAuthenticationPolicies.Validate(policy, _options).ShouldBeNull();
    }

    [Fact]
    public void Validate_AcceptsALegacyPolicy()
    {
        MachineAuthenticationPolicies.Validate(new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.ClientCredentials }, _options).ShouldBeNull();
    }

    [Fact]
    public void Validate_RejectsAPolicyWithoutAnyKind()
    {
        MachineAuthenticationPolicies.Validate(new MachineAuthenticationPolicy { EnabledCredentialKinds = [] }, _options)
            .ShouldBe("Enable at least one machine credential kind.");
        MachineAuthenticationPolicies.Validate(null, _options).ShouldNotBeNull();
    }

    [Fact]
    public void Validate_RejectsAnUnknownKind()
    {
        var policy = new MachineAuthenticationPolicy { EnabledCredentialKinds = [MachineCredentialKind.ApiKey, (MachineCredentialKind)7] };

        MachineAuthenticationPolicies.Validate(policy, _options).ShouldBe("The machine credential kind 7 doesn't exist.");
    }

    [Fact]
    public void Validate_RejectsADefaultThatIsNotEnabled()
    {
        var policy = new MachineAuthenticationPolicy
        {
            EnabledCredentialKinds = [MachineCredentialKind.Certificate],
            CredentialKind = MachineCredentialKind.ApiKey,
        };

        MachineAuthenticationPolicies.Validate(policy, _options).ShouldBe("The default credential kind ApiKey is not one of the enabled kinds.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3651)]
    public void Validate_RejectsALifetimeOutOfBounds(int days)
    {
        var policy = new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.Certificate, CertificateLifetimeDays = days };

        MachineAuthenticationPolicies.Validate(policy, _options).ShouldBe("The certificate lifetime must be between 1 and 3650 days.");
    }

    [Fact]
    public void Normalize_OrdersTheKindsAndFillsTheDefaultButKeepsAMissingLifetime()
    {
        var requested = new MachineAuthenticationPolicy
        {
            EnabledCredentialKinds = [MachineCredentialKind.CertificateAndApiKey, MachineCredentialKind.ApiKey],
        };

        var normalized = MachineAuthenticationPolicies.Normalize(requested);

        normalized.EnabledCredentialKinds.ShouldBe([MachineCredentialKind.ApiKey, MachineCredentialKind.CertificateAndApiKey]);
        normalized.CredentialKind.ShouldBe(MachineCredentialKind.CertificateAndApiKey);
        normalized.CertificateLifetimeDays.ShouldBeNull();
    }

    [Fact]
    public void Normalize_LegacyPolicy_ListsItsKind()
    {
        var normalized = MachineAuthenticationPolicies.Normalize(new MachineAuthenticationPolicy { CredentialKind = MachineCredentialKind.Certificate });

        normalized.EnabledCredentialKinds.ShouldBe([MachineCredentialKind.Certificate]);
        normalized.CredentialKind.ShouldBe(MachineCredentialKind.Certificate);
    }

    [Theory]
    [InlineData(MachineCredentialKind.ApiKey, true)]
    [InlineData(MachineCredentialKind.Certificate, true)]
    [InlineData(MachineCredentialKind.CertificateAndApiKey, false)]
    [InlineData(MachineCredentialKind.ClientCredentials, false)]
    public void IsEnabled_ChecksTheEnabledKinds(MachineCredentialKind kind, bool expected)
    {
        var policy = new MachineAuthenticationPolicy { EnabledCredentialKinds = [MachineCredentialKind.ApiKey, MachineCredentialKind.Certificate] };

        policy.IsEnabled(kind).ShouldBe(expected);
    }

    [Theory]
    [InlineData(MachineCredentialKind.ApiKey, false, true, true)]
    [InlineData(MachineCredentialKind.ApiKey, true, false, false)]
    [InlineData(MachineCredentialKind.Certificate, true, false, true)]
    [InlineData(MachineCredentialKind.Certificate, false, true, false)]
    [InlineData(MachineCredentialKind.CertificateAndApiKey, true, true, true)]
    [InlineData(MachineCredentialKind.CertificateAndApiKey, false, true, false)]
    [InlineData(MachineCredentialKind.CertificateAndApiKey, true, false, false)]
    [InlineData(MachineCredentialKind.ClientCredentials, true, true, false)]
    public void IsSatisfiedBy_RequiresWhatTheKindNeeds(MachineCredentialKind kind, bool hasCertificate, bool hasApiKey, bool expected)
    {
        kind.IsSatisfiedBy(hasCertificate, hasApiKey).ShouldBe(expected);
    }

    [Theory]
    [InlineData(MachineCredentialKind.ApiKey, false)]
    [InlineData(MachineCredentialKind.Certificate, true)]
    [InlineData(MachineCredentialKind.CertificateAndApiKey, true)]
    [InlineData(MachineCredentialKind.ClientCredentials, false)]
    public void IssuesCertificate_ForTheCertificateKinds(MachineCredentialKind kind, bool expected)
    {
        kind.IssuesCertificate().ShouldBe(expected);
    }
}
