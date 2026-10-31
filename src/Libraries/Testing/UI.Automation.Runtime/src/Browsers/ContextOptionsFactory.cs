using System.Net;
using _42.Testing.UI.Automation.Runtime.Settings;
using Microsoft.Playwright;

namespace _42.Testing.UI.Automation.Runtime.Browsers;

// The options of every browser context of the session, from the Browser section: the device descriptor or the
// viewport, locale, time zone, color scheme and Target:BaseUrl as BaseURL (with a trailing slash, so relative paths
// stay below it). HTTPS errors are ignored for localhost only. Built once; Create adds the storage state and the video.
public sealed class ContextOptionsFactory
{
    private readonly BrowserNewContextOptions template;
    private readonly List<string> warnings = [];

    // Throws UiAutomationSettingsException when Browser:Device is not in devices.
    public ContextOptionsFactory(UiAutomationSettings settings, DeviceCatalog devices)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(devices);

        var browser = settings.Browser;
        Engine = settings.EffectiveEngine;

        if (string.IsNullOrWhiteSpace(browser.Device))
        {
            template = new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = browser.Viewport.Width, Height = browser.Viewport.Height },
            };
        }
        else
        {
            if (!devices.TryGet(browser.Device, out var descriptor))
            {
                throw new UiAutomationSettingsException(UnknownDevice(browser.Device, devices));
            }

            template = descriptor;
            Device = browser.Device;

            if (Engine == BrowserEngine.Firefox && template.IsMobile == true)
            {
                // Playwright refuses isMobile in Firefox; the viewport, user agent and touch of the device stay.
                template.IsMobile = null;
                warnings.Add($"Firefox does not emulate isMobile; Browser:Device '{browser.Device}' runs with its viewport, user agent and touch only.");
            }
        }

        if (browser.Locale is not null)
        {
            template.Locale = browser.Locale;
        }

        if (browser.TimeZone is not null)
        {
            template.TimezoneId = browser.TimeZone;
        }

        if (browser.ColorScheme is not null)
        {
            template.ColorScheme = browser.ColorScheme;
        }

        if (!string.IsNullOrWhiteSpace(settings.Target.BaseUrl))
        {
            template.BaseURL = settings.Target.BaseUrl.EndsWith('/') ? settings.Target.BaseUrl : settings.Target.BaseUrl + "/";
            if (IsLocalHost(template.BaseURL))
            {
                template.IgnoreHTTPSErrors = true;
            }
        }
    }

    // The device of Browser:Device, null for a desktop viewport.
    public string? Device { get; }

    public BrowserEngine Engine { get; }

    // The effective viewport (the device's or Browser:Viewport), e.g. for page objects that branch on the width.
    public ViewportSize? Viewport => template.ViewportSize;

    // A device descriptor with isMobile or touch (Firefox keeps the touch of a phone).
    public bool IsMobile => template.IsMobile == true || template.HasTouch == true;

    public IReadOnlyList<string> Warnings => warnings;

    // storageState: the JSON of a signed-in state (StorageStateCache); recordVideoDirectory: where Playwright records.
    public BrowserNewContextOptions Create(string? storageState = null, string? recordVideoDirectory = null)
    {
        var options = new BrowserNewContextOptions(template)
        {
            StorageState = storageState,
        };

        if (recordVideoDirectory is not null)
        {
            options.RecordVideoDir = recordVideoDirectory;
            if (template.ViewportSize is { } viewport)
            {
                options.RecordVideoSize = new RecordVideoSize { Width = viewport.Width, Height = viewport.Height };
            }
        }

        return options;
    }

    internal static bool IsLocalHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.IsLoopback
            || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));
    }

    private static string UnknownDevice(string device, DeviceCatalog devices)
    {
        var similar = devices.Similar(device);
        return similar.Count == 0
            ? $"Browser:Device '{device}' is not a Playwright device descriptor."
            : $"Browser:Device '{device}' is not a Playwright device descriptor; similar: {string.Join(", ", similar)}.";
    }
}
