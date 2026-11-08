using _42.Testing.UI.Automation.Runtime.Access;
using _42.Testing.UI.Automation.Runtime.Users;
using Microsoft.Playwright;

namespace _42.Platform.Automation.Shared.Access;

// The base of a Platform project's Target:SignIn AuthKit flow, the real sign-in of a deployed target: the runtime runs
// the flow once per pool user per session and gives the user's later tests (and TestScope.OpenBrowserAsync) the captured
// storage state. The derived class leads the page from its application to the AuthKit hosted page and back
// (SignInAsync, with AuthKitHostedPage for the steps on the hosted page) and tells a page that shows the user signed out
// (IsSignedOutAsync). AuthKit keeps its session in a cookie on the auth domain, and authkit-js rotates the refresh token
// it keeps in local storage (devMode, on localhost) on every page load, so the runtime carries the cookies and the
// workos:* items of a test that ended signed in over to the user's next test; the application's other items stay as
// captured right after the sign-in.
public abstract class AuthKitSignInFlow : ISignInFlow
{
    // What Target:SignIn names.
    public const string FlowName = "AuthKit";

    // The keys authkit-js keeps in local storage: workos:refresh-token:<client id> and others.
    public const string StoragePrefix = "workos:";

    public string Name => FlowName;

    public abstract Task SignInAsync(IPage page, TestUser user, CancellationToken cancellationToken);

    // Not signed out by default; the derived class knows its application's signed-out pages.
    public virtual ValueTask<bool> IsSignedOutAsync(IPage page, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(false);
    }

    public bool IsSignInStorageItem(string origin, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.StartsWith(StoragePrefix, StringComparison.Ordinal);
    }
}
