using System.Diagnostics.CodeAnalysis;
using Microsoft.Playwright;

namespace _42.Testing.UI.Automation.Runtime.Browsers;

// The device descriptors Browser:Device may name ("Pixel 7", "iPhone 15"); Playwright's own list comes from its driver,
// so tests build one from a dictionary.
public sealed class DeviceCatalog
{
    private readonly IReadOnlyDictionary<string, BrowserNewContextOptions> devices;

    public DeviceCatalog(IReadOnlyDictionary<string, BrowserNewContextOptions> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        this.devices = devices;
    }

    public static DeviceCatalog Empty { get; } = new(new Dictionary<string, BrowserNewContextOptions>());

    public IReadOnlyCollection<string> Names => devices.Keys.ToArray();

    public static DeviceCatalog From(IPlaywright playwright)
    {
        ArgumentNullException.ThrowIfNull(playwright);
        return new DeviceCatalog(playwright.Devices);
    }

    // The exact name first, then the name in any case. The descriptor is a copy.
    public bool TryGet(string name, [NotNullWhen(true)] out BrowserNewContextOptions? descriptor)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!devices.TryGetValue(name, out var found))
        {
            found = devices.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
        }

        descriptor = found is null ? null : new BrowserNewContextOptions(found);
        return descriptor is not null;
    }

    // Names that share the first word of name, for the error of an unknown device.
    public IReadOnlyList<string> Similar(string name, int count = 5)
    {
        var word = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name;
        return devices.Keys
            .Where(key => key.Contains(word, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Take(count)
            .ToArray();
    }
}
