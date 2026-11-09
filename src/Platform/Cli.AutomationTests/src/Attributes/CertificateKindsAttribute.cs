using TUnit.Core;

// In the root namespace like AreaAttribute, so a test writes [CertificateKinds] without a using.
namespace _42.Platform.Cli.AutomationTests;

// A test that enables the credential kind Certificate or CertificateAndApiKey, or creates, renews or signs in with such a
// machine or a shared certificate: the category CertificateKinds. A deployment that turns these kinds off
// (MachineAuth:DisabledCredentialKinds, e.g. one without mTLS) refuses them, so a run against it leaves these tests
// out with --treenode-filter "/**[Category!=CertificateKinds]".
public sealed class CertificateKindsAttribute() : CategoryAttribute(CliCategories.CertificateKinds);
