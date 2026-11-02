using TUnit.Core;

// In the root namespace like AreaAttribute, so a test writes [DesktopOnly] without a using.
namespace _42.Platform.UI.Admin.AutomationTests;

// A test of the desktop layout (the docked sidebar, the grid's row menus, selection, hover): the category DesktopOnly,
// which PlatformTest.GetSkipReason skips before the lease when the session's viewport is narrower than
// App.DockedSidebarWidth (1024 px), e.g. on the mobile profile.
public sealed class DesktopOnlyAttribute() : CategoryAttribute(PlatformCategories.DesktopOnly);
