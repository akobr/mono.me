using _42.Testing.UI.Automation.Runtime.Users;
using Microsoft.Playwright;

namespace _42.Testing.UI.Automation.Runtime.Access;

// Signs a pool user in through the application's real sign-in page; implemented by the consuming project (AuthKit for
// the Platform). The StorageStateCache runs it once per user per session in a throw-away, untraced context and gives
// every later test of that user the captured storage state, refreshed by the tests that ended signed in.
public interface ISignInFlow
{
    // What Target:SignIn names, e.g. "AuthKit".
    string Name { get; }

    // Whether Users:Pool:<n>:Password is required for this flow.
    bool RequiresPassword => true;

    // Starts on a blank page of a context whose BaseURL is Target:BaseUrl and returns once the application shows the
    // user as signed in. Throws when the sign-in fails.
    Task SignInAsync(IPage page, TestUser user, CancellationToken cancellationToken);

    // Whether the page shows the user as signed out (e.g. it was sent to the identity provider). Asked at the end of
    // every test that started signed in; true drops the user's cached state, so the next test of the user signs in again.
    ValueTask<bool> IsSignedOutAsync(IPage page, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(false);
    }

    // Whether a local storage item belongs to the sign-in, e.g. a refresh token the identity provider rotates on use.
    // When a test ends signed in, these items and every cookie of its context replace those of the cached state, so the
    // next test of the user starts from tokens that are still valid; the application's other items (preferences) stay
    // as captured right after the sign-in. None by default: only the cookies are carried over.
    bool IsSignInStorageItem(string origin, string name)
    {
        return false;
    }
}
