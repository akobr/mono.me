using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Schema tab of a configuration (components/configuration/CombinedSchemaView.vue): the schemas that apply to the
// annotation, merged. Without one the empty state "No schema applies"; otherwise the heading "Applied schemas, in
// order" over an ordered list (per layer its index, the badge "Type", "Descendant type" or "Annotation", the type's
// name or the annotation's key, a link to Modeling's schema for Contributors and up, plain text below, and "v<n>"),
// then the read-only editor "Combined schema". A deployed target's type schema can add a layer: find a layer by its
// label, never by its index.
public sealed partial class CombinedSchemaView(App app, ILocator scope)
{
    public ILocator AppliedSchemasHeading => scope.GetByRole(AriaRole.Heading, new() { Name = "Applied schemas, in order", Exact = true });

    public ILocator NoSchemaHeading => scope.GetByRole(AriaRole.Heading, new() { Name = "No schema applies", Exact = true });

    public CodeEditor Combined => new(app, "Combined schema", scope);

    // The layer of a type (its name, e.g. "Execution") or an annotation (its key), by its exact label.
    public ILocator Layer(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return scope.GetByRole(AriaRole.Listitem).Filter(new() { Has = app.Page.GetByText(label, new() { Exact = true }) });
    }

    // The layer's link to its schema in Modeling (Contributor and up).
    public ILocator LayerLink(string label)
    {
        return Layer(label).GetByRole(AriaRole.Link, new() { Name = label, Exact = true });
    }

    // A text of the layer by its exact value: its kind ("Annotation") or its version ("v1").
    public ILocator LayerText(string label, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return Layer(label).GetByText(text, new() { Exact = true });
    }
}
