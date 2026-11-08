using _42.Platform.Cli.AutomationTests.Running;

namespace _42.Platform.Cli.AutomationTests.Access;

// What a device code sign-in did: the run of sform account, the code it printed (null when it printed none, e.g. "You
// are already logged in"), and the steps taken on the device page.
public sealed record DeviceCodeSignInResult(SformResult Result, DeviceCodePrompt? Prompt, IReadOnlyList<string> Steps)
{
    // sform signed in, or was signed in already: exit 0, or 43 when no default project is set (AccountCommand).
    public bool IsSignedIn => Result.ExitCode is 0 or 43;

    public bool WasSignedInAlready => Prompt is null && IsSignedIn;
}
