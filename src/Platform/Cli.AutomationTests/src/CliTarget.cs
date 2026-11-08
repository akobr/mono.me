using System.Text.RegularExpressions;

namespace _42.Platform.Cli.AutomationTests;

// The CLI's keys of the Target section, next to the library's BaseUrl (here the API's base, /api included, e.g.
// http://localhost:7071/api) and SignIn (None: the device page of the sign-in asks for the credentials), and the
// Platform's shared ones (PlatformTargetSettings: the organization, project and view the tests work in, and the projects
// of the exact roles). Read with Settings.Bind<CliTarget>("Target"); the defaults are in testsettings.json.
public sealed partial class CliTarget : PlatformTargetSettings
{
    private List<string> features = [];

    // The WorkOS organization id (org_…) the pool users sign in to: passed as sform account -o, which takes the WorkOS id,
    // not the 2S organization name of Target:Organization. Null signs in without -o (a pool user in one organization).
    public string? AuthKitOrganizationId { get; set; }

    // What the target offers beyond the basics, for the cases that need it ([RequiresFeature]): Byok (a customer key in
    // an Azure Key Vault), Hsm (HSM-backed keys), Registration (an unregistered pool user whose account the run may
    // create), MultiOrganization (a pool user in two organizations).
    // Case-insensitive.
    public List<string> Features
    {
        get => features;
        set => features = value ?? [];
    }

    // "<organization>.<project>", as sform's -p takes it.
    public string ProjectKey => $"{Organization}.{Project}";

    public bool HasFeature(string feature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        return Features.Any(candidate => string.Equals(candidate?.Trim(), feature, StringComparison.OrdinalIgnoreCase));
    }

    // Adds a message to errors for each CLI key that is wrong, after the shared ones (PlatformTargetSettings.Validate).
    public void ValidateCli(ICollection<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        Validate(errors);

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var api) || (api.Scheme != Uri.UriSchemeHttp && api.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add($"{Section}:BaseUrl '{BaseUrl}' is not the API's base URL, e.g. http://localhost:7071/api or https://<host>/api.");
        }

        if (AuthKitOrganizationId is { } organization && !OrganizationIdPattern().IsMatch(organization))
        {
            errors.Add($"{Section}:AuthKitOrganizationId '{organization}' is not a WorkOS organization id (org_ and 26 letters and digits); it is what sform account -o takes, not the 2S organization name. Leave it null for pool users in one organization.");
        }

        foreach (var feature in Features.Where(feature => !CliFeatures.All.Contains(feature?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)))
        {
            errors.Add($"{Section}:Features has '{feature}', which no test asks for; the features are {string.Join(", ", CliFeatures.All)}.");
        }
    }

    [GeneratedRegex(@"^org_[0-9A-Za-z]{10,40}\z", RegexOptions.CultureInvariant)]
    private static partial Regex OrganizationIdPattern();
}
