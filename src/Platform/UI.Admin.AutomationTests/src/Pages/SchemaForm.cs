using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The form a configuration's schema generates in Form mode (components/configuration/SchemaForm.vue, JSON Forms with
// the renderers of components/configuration/renderers). Every control is framed by ControlField: a div whose
// data-path is the control's JSON Pointer ("/retries", "/tiers/0/name", "/headers/x-run"), the best scope there is
// (JSON Forms gives the controls no other stable name), with a label (the title or the property in Start Case, plus
// " *" when required), the badge "inherited" and the buttons "Override" and "Reset to inherited" for a value of the
// effective document that the saved own content lacks, "fx" and the error under it. A number commits on blur or
// Enter (UInputNumber), a text on every input; an array of objects has cards titled by the first string value or
// "Item <n>" with "Move up", "Move down", "Remove" and "Add item"; a map has a row per key (the textbox "Key", the
// value, "Remove <key>") and "New key" with "Add".
public sealed partial class SchemaForm(ILocator scope)
{
    // The frame of the control at the pointer, e.g. "/retries".
    public ILocator Field(string pointer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pointer);
        return scope.Locator($"[data-path=\"{pointer}\"]");
    }

    // The control's label, the first of its frame (a nested control's labels follow it).
    public ILocator Label(string pointer)
    {
        return Field(pointer).Locator("label").First;
    }

    // The input of a number or integer control (UInputNumber).
    public ILocator NumberInput(string pointer)
    {
        return Field(pointer).GetByRole(AriaRole.Spinbutton).First;
    }

    // The input of a text control; for a container (an array, a map) the first text input inside it.
    public ILocator TextInput(string pointer)
    {
        return Field(pointer).GetByRole(AriaRole.Textbox).First;
    }

    // The switch of a boolean control.
    public ILocator Switch(string pointer)
    {
        return Field(pointer).GetByRole(AriaRole.Switch).First;
    }

    public ILocator InheritedBadge(string pointer)
    {
        return Field(pointer).GetByText("inherited", new() { Exact = true }).First;
    }

    public ILocator OverrideButton(string pointer)
    {
        return Button(pointer, "Override");
    }

    public ILocator ResetToInheritedButton(string pointer)
    {
        return Button(pointer, "Reset to inherited");
    }

    // A button inside the control's frame by its exact name ("Add item", "Move up", "Add", "Remove x-run", a card's
    // title, …).
    public ILocator Button(string pointer, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Field(pointer).GetByRole(AriaRole.Button, new() { Name = name, Exact = true });
    }

    // The frame's "fx" (not on a binding value): turns the value into a binding, "@" and the text it had.
    public ILocator FxButton(string pointer)
    {
        return Button(pointer, "fx");
    }

    // The leading "fx" of a binding value's input, named "Binding reference": it opens the reference with "Use".
    public ILocator BindingReferenceButton(string pointer)
    {
        return Button(pointer, "Binding reference");
    }

    // The line under a binding value that explains it ("Function config: Reads a value …") or warns of a likely
    // mistake. A data attribute: the line has no role or name.
    public ILocator BindingExplanation(string pointer)
    {
        return Field(pointer).Locator("[data-binding-explanation]");
    }

    // The note under every binding value.
    public ILocator BindingNote(string pointer)
    {
        return Field(pointer).GetByText("Evaluated by the server; schema checks run after evaluation.", new() { Exact = true });
    }

    // Turns a binding value back into a literal: the text without its leading "@".
    public ILocator ConvertToValueButton(string pointer)
    {
        return Button(pointer, "Convert to value");
    }

    // Fills the input and leaves it with Tab, so a number control commits its value.
    public async Task FillAsync(ILocator input, string value)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(value);

        await input.FillAsync(value);
        await input.PressAsync("Tab");
    }
}
