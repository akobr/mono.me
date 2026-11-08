# 42.Testing.UI.Automation.Runtime.TUnit

The **TUnit** adapter of the 42 UI automation framework (Playwright, BrowserStack, Testiny). It references `TUnit.Core`; the test project references `TUnit` (engine and entry point) and runs with `dotnet run`.

- `UiTest`: the base class of a UI test, with `Page`, `Context`, the leased `User`, `Settings`, seeded `Data`, `RequiredUser`, `SignedIn`, `CleanUpAsync` and `LeaseAnotherUserAsync`; its artifacts are attached to TUnit's HTML and TRX reports.
- `AutomationTest`: the base class of a test without a page (e.g. one that runs a command line), with everything of `UiTest` except `Page`, `Context` and `SignedIn`; it starts no browser, and `OpenBrowserAsync` opens a context only when the test asks for one (`UiTest` derives from it).
- The session hooks run from the library: settings before discovery (`Execution:MaxParallelTests` goes to TUnit, and TUnit's hook timeout is raised to the settings' start budget, so a slow lease or BrowserStack connection ends Blocked with its own message), the session (and the setup's target check) before the first test, the reporters' completion after the last. The `ui-automation:` log shows the settings' secrets as `***`.
- Attributes: `[Elementary]`, `[Area("…")]`, `[Serial]`, `[RequiresProvider]`, `[RequiresEngine]`, and for the assembly `[UiAutomationSetup<T>]`, `[ReportResults]`, `[ConfiguredRetry]` (retries per `Execution:Retries`) and `[BrowserStackSessionLimit]`.

```csharp
[assembly: UiAutomationSetup<ShopAutomationSetup>]
[assembly: ReportResults]
[assembly: ConfiguredRetry]
[assembly: BrowserStackSessionLimit]

[Area("Checkout")]
public sealed class CheckoutTests : UiTest
{
    [Test]
    [Elementary]
    public async Task Cart_ShowsTheItem()
    {
        await Page.GotoAsync("cart");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Cart" })).ToBeVisibleAsync();
    }
}
```

```bash
dotnet run --project src/Shop/UI.AutomationTests -- --treenode-filter "/**[Category=Elementary]"
```

Documentation: [UI Automation](https://github.com/akobr/mono.me/blob/main/docs/Libraries/Testing/UI.Automation/readme.md), with the recipe to adopt it in a project.
