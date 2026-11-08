using System.Text.RegularExpressions;

namespace _42.Platform.Cli.AutomationTests;

// The KeyValues section: the target's customer key for the cases of the Byok feature (KV-POL-05, KV-POL-07). Read with
// Settings.Bind<KeyValueSettings>("KeyValues"); null in testsettings.json, set in a profile, testsettings.local.json, the
// user secrets or UITESTS_KeyValues__ByokKeyUri and UITESTS_KeyValues__ByokTenantId. CliAutomationSetup.Validate
// requires both when Target:Features lists Byok, and checks the shape of what is set.
public sealed partial class KeyValueSettings
{
    public const string Section = "KeyValues";

    // What a run that lists Byok without the customer key is told.
    public const string MissingCustomerKey =
        $"Target:Features lists {CliFeatures.Byok}, so the tests need the target's customer key: set {Section}:ByokKeyUri (https://<vault>.vault.azure.net/keys/<name>, a key the platform's application may wrap and unwrap with) and {Section}:ByokTenantId (the Entra tenant id of the vault) in testsettings.<profile>.json, the user secrets or UITESTS_{Section}__ByokKeyUri and UITESTS_{Section}__ByokTenantId (docs/Platform/Cli.AutomationTests/automation-tests.md, \"KeyValues\").";

    // The versionless URI of a key in an Azure Key Vault that the platform's application may wrap and unwrap with:
    // https://<vault>.vault.azure.net/keys/<name>.
    public string? ByokKeyUri { get; set; }

    // The Entra tenant id (a GUID) of the vault.
    public string? ByokTenantId { get; set; }

    // Adds a message to errors for each key that is wrong: both are required when the target lists Byok, and a key that is
    // set must be an https key URI without a version, the tenant a GUID.
    public void Validate(CliTarget target, ICollection<string> errors)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(errors);

        if (target.HasFeature(CliFeatures.Byok) && (string.IsNullOrWhiteSpace(ByokKeyUri) || string.IsNullOrWhiteSpace(ByokTenantId)))
        {
            errors.Add(MissingCustomerKey);
        }

        if (!string.IsNullOrWhiteSpace(ByokKeyUri)
            && (!Uri.TryCreate(ByokKeyUri.Trim(), UriKind.Absolute, out var key) || key.Scheme != Uri.UriSchemeHttps || !KeyPathPattern().IsMatch(key.AbsolutePath) || key.Query.Length > 0))
        {
            errors.Add($"{Section}:ByokKeyUri '{ByokKeyUri}' is not the versionless URI of a Key Vault key: https://<vault>.vault.azure.net/keys/<name>.");
        }

        if (!string.IsNullOrWhiteSpace(ByokTenantId) && !Guid.TryParse(ByokTenantId.Trim(), out _))
        {
            errors.Add($"{Section}:ByokTenantId '{ByokTenantId}' is not an Entra tenant id (a GUID).");
        }
    }

    [GeneratedRegex(@"^/keys/[0-9A-Za-z-]+/?\z", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPathPattern();
}
