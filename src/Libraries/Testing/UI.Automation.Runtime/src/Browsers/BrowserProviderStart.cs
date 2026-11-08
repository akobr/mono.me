namespace _42.Testing.UI.Automation.Runtime.Browsers;

// When the session starts its browser provider (AutomationSessionOptions.BrowserProviderStart).
public enum BrowserProviderStart
{
    // With the session: Playwright's driver starts, the local browser launches, and one that does not fails the start
    // before any test.
    WithSession,

    // At the first browser a scope asks for (a test that opens a page, TestScope.OpenBrowserAsync, a target check with a
    // page). A session whose tests never ask for one never starts Playwright and needs no installed browser; a provider
    // that does not start fails each test that asks for a browser (Blocked) instead of the session.
    OnFirstLease,
}
