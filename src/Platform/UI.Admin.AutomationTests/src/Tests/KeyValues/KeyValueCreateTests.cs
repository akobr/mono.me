using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.KeyValues;

// Create: a new key of the values store with its value, public or secret.
[Area(Area.KeyValues)]
public sealed class KeyValueCreateTests : PlatformTest
{
    // KV-CRE-01: Create a public key value and read it back.
    // A new public key value (its key "at_<runId>.<word>_<hex4>": the store allows no '-') opens its detail with the
    // value, and its Versions tab has the first version as the current one.
    [Test]
    [Elementary]
    [TestCase(4)]
    [UserRole(Role.Editor)]
    public async Task KeyValues_CreateAndRead()
    {
        var key = PlatformData.KeyValueKey();
        var value = PlatformData.Slug("value");

        var keyValues = new KeyValuesPage(App, Routes);
        await keyValues.OpenAsync();
        await keyValues.CreateAsync(key, value);
        await Expect(keyValues.Value(key)).ToHaveValueAsync(value);

        await keyValues.OpenVersionsAsync(key);
        await Expect(keyValues.CurrentVersion).ToContainTextAsync("v1(current)");
    }
}
