using _42.Testing.UI.Automation.Runtime.Settings;
using Microsoft.Playwright;

namespace _42.Testing.UI.Automation.Runtime.Browsers;

// How a remote provider reaches its grid: BrowserType.ConnectAsync of a Playwright driver, a fake in the unit tests.
internal interface IRemoteBrowserConnector : IAsyncDisposable
{
    // The device descriptors of the driver, for Browser:Device.
    DeviceCatalog Devices { get; }

    Task<IBrowser> ConnectAsync(BrowserEngine engine, string endpoint, BrowserTypeConnectOptions options, CancellationToken cancellationToken);
}
