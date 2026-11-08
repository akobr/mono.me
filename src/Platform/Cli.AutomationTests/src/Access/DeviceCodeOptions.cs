namespace _42.Platform.Cli.AutomationTests.Access;

// How DeviceCodeSignIn runs a sign-in; the defaults sign the user in with sform account [-o Target:AuthKitOrganizationId].
public sealed record DeviceCodeOptions
{
    // The command, "account" by default (also "login" or "access", the aliases).
    public IReadOnlyList<string> Command { get; init; } = ["account"];

    // -o: null passes Target:AuthKitOrganizationId when set; an empty string passes no -o; any other value passes it
    // (e.g. an organization the user is not a member of).
    public string? Organization { get; init; }

    // The spelling of the option, -o or --organization.
    public string OrganizationOption { get; init; } = "-o";

    // More arguments after the command, e.g. -b.
    public IReadOnlyList<string> Arguments { get; init; } = [];

    // More variables (null removes one), e.g. SFORM_BROWSER.
    public Dictionary<string, string?> Environment { get; init; } = new(StringComparer.Ordinal);

    // Decline the request on the device page instead of approving it (SGN-DEV-05): sform then ends with 44.
    public bool Decline { get; init; }

    // true: a sign-in that does not end signed in (0, or 43 without a default project) after the approval throws. false
    // returns the result whatever sform ended with, for a case that expects a failure after the approval (SGN-DEV-06:
    // several organizations, exit 2 and the list; SGN-DEV-07: -o of an organization the user is not in, exit 2); the home
    // is marked signed in only for 0 and 43.
    public bool ExpectSignedIn { get; init; } = true;

    // Start the device page's browser from the user's AuthKit session of an earlier approval of this run, when there is
    // one (AuthKitSessionCache); false always starts anonymous and signs in on the hosted page.
    public bool UseCachedBrowserSession { get; init; } = true;

    // Read the code from the JSON line (SFORM_DEVICE_CODE_OUTPUT=json); false reads it from the text prompt on the
    // standard output (SGN-DEV-03).
    public bool JsonPrompt { get; init; } = true;

    // How long the approval may take on the device page; null: three quarters of the run's timeout (Cli:TimeoutMs), at
    // least 30 s, so the page is recorded before the run's timeout kills sform.
    public TimeSpan? ApprovalTimeout { get; init; }
}
