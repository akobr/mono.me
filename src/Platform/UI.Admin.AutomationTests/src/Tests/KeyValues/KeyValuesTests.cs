using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation.Runtime.TUnit;
using _42.Testing.UI.Automation.Runtime.Users;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.KeyValues;

[Area(Area.KeyValues)]
public sealed class KeyValuesTests : PlatformTest
{
    // New key is Contributor and up.
    protected override UserRequirement RequiredUser => EditingUser;

    // A new public key value (its key "at_<runId>.<word>_<hex4>": the store allows no '-') opens its detail with the
    // value, and its Versions tab has the first version as the current one.
    [Test]
    [Elementary]
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
