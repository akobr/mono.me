using Microsoft.Playwright;

namespace _42.Testing.UI.Automation.Runtime.Browsers;

// The browser one test uses until it is released. RemoteSessionName: the name of a remote session (BrowserStack), null
// for a local browser. Properties go into the test's report.
public sealed record BrowserLease(IBrowser Browser, string? RemoteSessionName, IReadOnlyDictionary<string, string> Properties);
