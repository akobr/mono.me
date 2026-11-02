using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// "Roles" in the top bar of a Members page (RoleLegend.vue; icon-only below 640 px with the same name): a popover, a
// dialog named by its button, that lists the assignable roles as their badges (terms) with a description each.
public sealed partial class RoleLegend(App app)
{
    // The badges of the roles, in their order.
    public static readonly IReadOnlyList<string> RoleLabels = ["Reader", "Contributor", "Contributor + secrets", "Administrator", "Owner"];

    // The descriptions of the roles (app/utils/roles.ts), in the order of RoleLabels.
    public static readonly IReadOnlyList<string> RoleDescriptions =
    [
        "Reads annotations, configurations, templates and schemas.",
        "Edits annotations, configurations, templates and schemas; manages machines.",
        "Like Contributor, and reads resolved configurations with secrets.",
        "Manages members, invitations, shared certificates, the machine policy and views.",
        "Everything, including granting Owner and creating projects in the organization.",
    ];

    public ILocator Button => app.Page.GetByRole(AriaRole.Button, new() { Name = "Roles", Exact = true });

    public ILocator Root => app.Page.GetByRole(AriaRole.Dialog, new() { Name = "Roles", Exact = true });

    public ILocator Roles => Root.GetByRole(AriaRole.Term);

    public ILocator Descriptions => Root.GetByRole(AriaRole.Definition);

    public async Task OpenAsync()
    {
        await Button.ClickAsync();
        await Expect(Root).ToBeVisibleAsync();
    }

    // Closes the popover with Escape.
    public async Task CloseAsync()
    {
        await app.Page.Keyboard.PressAsync("Escape");
        await Expect(Root).ToBeHiddenAsync();
    }
}
