using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Configurations;

// Bindings and Resolved: binding expressions ("@…") in a configuration. The Resolved tab shows the document the server
// evaluated, covered until Reveal, or the error of a binding it cannot evaluate; the JSON editor completes and explains
// @config, and a form field becomes a binding with fx. The bindings use only the function config, which every target
// registers (the mock and a deployed server); the rest of a server's catalog differs.
[Area(Area.Configurations)]
public sealed partial class ConfigurationBindingTests : PlatformTest
{
    // The Effective tab shows the binding as written: a JSON string with escaped quotes.
    private const string UnevaluatedUrl = "\"url\": \"@config(\\\"/base\\\")\"";

    private const string EndpointSchema = """
        {"type":"object","properties":{"endpoint":{"type":"string","title":"Endpoint"}}}
        """;

    // CFG-BND-01: Resolved is hidden until Reveal and shows the evaluated bindings.
    // A url bound to the document's own base: Resolved warns that it may contain secrets and covers the document (out
    // of the accessibility tree) until Reveal, which shows the url evaluated; Effective keeps the binding as written,
    // and another visit of Resolved covers the document again.
    [Test]
    [TestCase(214)]
    [UserRole(Role.Editor)]
    public async Task Configurations_Resolved_RevealShowsEvaluatedBindings()
    {
        var key = await ConfigurationSteps.CreateResponsibilityAsync(App, Routes, PlatformData);
        var origin = $"https://{PlatformData.RunSlug}.example";
        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenAsync(key);
        await ConfigurationSteps.StoreVersionsAsync(App, configuration, $$"""{"base": "{{origin}}", "url": "@config(\"/base\")"}""");
        await MovePointerOffBindingsAsync();

        await configuration.OpenTabAsync("Resolved");
        var resolved = configuration.ResolvedView;
        await resolved.WaitForResolvedAsync();
        await Expect(resolved.SecretsAlertTitle).ToBeVisibleAsync();
        await ExpectCoveredAsync(resolved);

        var document = await resolved.RevealAsync();
        await Expect(document.Content).ToContainTextAsync($"\"url\": \"{origin}\"");

        await configuration.OpenTabAsync("Effective");
        await Expect(configuration.EffectiveView.Document.Content).ToContainTextAsync(UnevaluatedUrl);

        await configuration.OpenTabAsync("Resolved");
        await resolved.WaitForResolvedAsync();
        await ExpectCoveredAsync(resolved);
    }

    // CFG-BND-02: A binding that cannot be evaluated shows the binding error.
    // The server refuses a binding it cannot evaluate (422): Resolved shows the error with the server's message instead
    // of the document. The mock refuses a @config of a missing path; a deployed server resolves that to null and
    // refuses it only inside an interpolation, so the binding follows the target. Once the value is plain again (v3),
    // Resolved offers Reveal.
    [Test]
    [TestCase(221)]
    [UserRole(Role.Editor)]
    public async Task Configurations_Resolved_ShowsBindingError()
    {
        var key = await ConfigurationSteps.CreateResponsibilityAsync(App, Routes, PlatformData);
        var broken = Target.IsMock
            ? """{"broken": "@config(\"/missing\")"}"""
            : """{"broken": "@[x @config(\"/missing\")]"}""";
        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenAsync(key);
        await ConfigurationSteps.StoreVersionsAsync(App, configuration, broken);
        await MovePointerOffBindingsAsync();

        await configuration.OpenTabAsync("Resolved");
        var resolved = configuration.ResolvedView;
        await resolved.WaitForResolvedAsync();
        await Expect(resolved.SecretsAlertTitle).ToBeVisibleAsync();
        await Expect(resolved.BindingError).ToBeVisibleAsync();
        await Expect(resolved.BindingErrorDescription).Not.ToBeEmptyAsync();
        await Expect(resolved.RevealButton).ToHaveCountAsync(0);
        await Expect(resolved.CoveredDocument).ToHaveCountAsync(0);

        await configuration.OpenEditTabAsync(key);
        await configuration.SetDocumentAsync("""{"broken": "fixed"}""");
        await configuration.SaveAsync(3);

        await configuration.OpenTabAsync("Resolved");
        await resolved.WaitForResolvedAsync();
        await Expect(resolved.RevealButton).ToBeVisibleAsync();
        await Expect(resolved.BindingError).ToHaveCountAsync(0);
    }

    // CFG-BND-03: The JSON editor completes @config paths, explains them on hover, and Bindings filters the reference.
    // "@con" in a string value offers config, whose argument lists the paths of the draft ({"count": 1} before the
    // partial text): picking /count writes the escaped binding. Hovering it explains the function; the toolbar's
    // Bindings reference (not insertable, so without Use) filters to config's entry, or to nothing.
    [Test]
    [TestCase(224)]
    [UserRole(Role.Editor)]
    public async Task Configurations_JsonEditor_CompletesAndExplainsBindings()
    {
        var key = await ConfigurationSteps.CreateResponsibilityAsync(App, Routes, PlatformData);
        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenAsync(key);
        await configuration.CreateEmptyAsync();
        await Expect(App.Toast("Configuration created")).ToBeVisibleAsync();

        var editor = configuration.Document;
        await configuration.SetDocumentAsync("""{"count": 1}""");
        await editor.ReplaceAsync("{\n  \"endpoint\": \"");
        await Expect(configuration.SaveButton).ToBeDisabledAsync();

        // Typed, not inserted: only key presses open the completion.
        await editor.TypeAsync("@con");
        await editor.BindingCompletionStartingWith("config").ClickAsync();
        await Expect(editor.BindingCompletion("/count")).ToBeVisibleAsync();

        await editor.BindingCompletion("/count").ClickAsync();
        await App.Page.Keyboard.PressAsync("End");
        await App.Page.Keyboard.InsertTextAsync("\"\n}");
        await Expect(editor.Content).ToContainTextAsync("\"endpoint\": \"@config(\\\"/count\\\")\"");

        await editor.HoverBindingAsync("Function config");
        await Expect(editor.BindingHoverCard).ToContainTextAsync("Reads a value of the configuration being resolved");
        await MovePointerOffBindingsAsync();

        var reference = configuration.BindingReference;
        await configuration.BindingsButton.ClickAsync();
        await Expect(reference.Region).ToBeVisibleAsync();
        await reference.FilterAsync("config");
        await Expect(reference.Entry("function-config")).ToBeVisibleAsync();
        await Expect(reference.UseButtons).ToHaveCountAsync(0);

        await reference.FilterAsync("zzzz-nomatch");
        await Expect(reference.NothingMatches("zzzz-nomatch")).ToBeVisibleAsync();
        await Expect(reference.Entry("function-config")).ToHaveCountAsync(0);
    }

    // CFG-BND-04: fx turns a form field into a binding, Use fills it, and Convert to value turns it back.
    // In Form mode (the annotation's schema has a plain string, so the binding passes the server's schema check) fx
    // makes the field a monospace binding "@"; the field's reference lists every section, and Use puts config's first
    // example in (the mock's and the server's differ, so only its start is checked). The binding saves as v2, JSON
    // shows it, and Convert to value leaves the text without "@", one change.
    [Test]
    [TestCase(227)]
    [UserRole(Role.Editor)]
    public async Task Configurations_FormBinding_UseAndConvertToValue()
    {
        var key = await ConfigurationSteps.CreateResponsibilityAsync(App, Routes, PlatformData);
        await ConfigurationSteps.CreateAnnotationSchemaAsync(App, Routes, key, EndpointSchema);

        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenAsync(key);
        await configuration.CreateEmptyAsync();
        await Expect(App.Toast("Configuration created")).ToBeVisibleAsync();
        await Expect(configuration.FormMode).ToBeCheckedAsync();

        var form = configuration.Form;
        var endpoint = form.TextInput("/endpoint");
        await form.FxButton("/endpoint").ClickAsync();
        await Expect(endpoint).ToHaveValueAsync("@");
        await Expect(endpoint).ToHaveCSSAsync("font-family", Monospace());
        await Expect(form.BindingNote("/endpoint")).ToBeVisibleAsync();
        await Expect(form.ConvertToValueButton("/endpoint")).ToBeVisibleAsync();

        var reference = configuration.BindingReference;
        await form.BindingReferenceButton("/endpoint").ClickAsync();
        await Expect(reference.Region).ToBeVisibleAsync();
        foreach (var section in new[] { "Expressions", "Functions", "Sources", "Object bindings" })
        {
            await Expect(reference.SectionHeading(section)).ToBeVisibleAsync();
        }

        await reference.Entry("function-config").GetByRole(AriaRole.Button, new() { Name = "Use", Exact = true }).First.ClickAsync();
        await Expect(reference.Region).ToBeHiddenAsync();
        await Expect(endpoint).ToHaveValueAsync(ConfigBinding());
        await Expect(form.BindingExplanation("/endpoint")).ToContainTextAsync("Function config: Reads a value of the configuration being resolved");

        await configuration.SaveWithShortcutAsync(endpoint, 2);

        await configuration.JsonMode.ClickAsync();
        await Expect(configuration.Editor).ToContainTextAsync("\"endpoint\": \"@config(");

        await configuration.FormMode.ClickAsync();
        await form.ConvertToValueButton("/endpoint").ClickAsync();
        await Expect(endpoint).ToHaveValueAsync(ConfigValue());
        await Expect(endpoint).Not.ToHaveCSSAsync("font-family", Monospace());
        await Expect(form.FxButton("/endpoint")).ToBeVisibleAsync();
        await Expect(form.BindingReferenceButton("/endpoint")).ToHaveCountAsync(0);
        await Expect(configuration.Changes(1)).ToBeVisibleAsync();
    }

    [GeneratedRegex("monospace")]
    private static partial Regex Monospace();

    [GeneratedRegex(@"^@config\(")]
    private static partial Regex ConfigBinding();

    [GeneratedRegex(@"^config\(")]
    private static partial Regex ConfigValue();

    // The resolved document is there but covered: out of the accessibility tree until Reveal.
    private static async Task ExpectCoveredAsync(ConfigurationResolved resolved)
    {
        await Expect(resolved.RevealButton).ToBeVisibleAsync();
        await Expect(resolved.CoveredDocument).ToBeVisibleAsync();
        await Expect(resolved.Document.Content).ToHaveCountAsync(0);
    }

    // The click into the editor leaves the pointer on the document: resting on a binding, it opens the binding's card
    // over the tab links.
    private async Task MovePointerOffBindingsAsync()
    {
        await App.Page.Mouse.MoveAsync(0, 0);
    }
}
