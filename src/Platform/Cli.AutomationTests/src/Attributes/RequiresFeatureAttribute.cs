using TUnit.Core;

// In the root namespace like AreaAttribute, so a test writes [RequiresFeature(CliFeatures.Byok)] without a using.
namespace _42.Platform.Cli.AutomationTests;

// A test of a feature not every target has (mode X of the catalogue): the category Requires-<Feature>, which
// CliTest.GetSkipReason skips before the lease unless Target:Features lists the feature (CliFeatures: Byok, Hsm,
// Registration, MultiOrganization). The gap is listed in coverage-exceptions.json (phase 4).
public sealed class RequiresFeatureAttribute(string feature) : CategoryAttribute(CliCategories.RequiresPrefix + feature)
{
    public string Feature { get; } = feature;
}
