using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// A section of a page that the #hash of an address names (composables/useHashScroll.ts): settings#secrets,
// machines#shared-certificates and members#invitations, which the old project pages /secrets, /certificates and
// /invitations redirect to. Once the content above it loaded, the page scrolls the section (section#<id>, tabindex -1)
// into view and focuses it, as a followed anchor would. The sections have no accessible name (no region role), so the
// section is found by its id and checked by its headings, whose names keep their case (only CSS makes them upper case).
public sealed partial class HashLinkedSection(App app, string id)
{
    public ILocator Root => app.Page.Locator($"section#{id}");

    // A heading inside the section by its exact name: its own h2 ("Secrets", "Shared certificates", "Invitations") or a
    // card's ("Allowed levels").
    public ILocator Heading(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Root.GetByRole(AriaRole.Heading, new() { Name = name, Exact = true });
    }

    // The address ends in #<id>, and the section is in view and has the focus; the page scrolls only once the content
    // above the section loaded, which the assertions wait for.
    public async Task ExpectLandedAsync()
    {
        await Expect(app.Page).ToHaveURLAsync(new Regex($"#{Regex.Escape(id)}$"));
        await Expect(Root).ToBeInViewportAsync();
        await Expect(Root).ToBeFocusedAsync();
    }
}
