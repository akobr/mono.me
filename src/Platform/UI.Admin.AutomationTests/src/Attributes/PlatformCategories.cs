// In the root namespace like the attributes that add these categories.
namespace _42.Platform.UI.Admin.AutomationTests;

// The Platform's TUnit categories next to the library's Elementary, Serial and Area-<area>; a run selects or leaves
// them out with --treenode-filter "/**[Category!=DisposableProject]".
public static class PlatformCategories
{
    // [DesktopOnly]: skipped below App.DockedSidebarWidth (1024 px).
    public const string DesktopOnly = "DesktopOnly";

    // [PhoneOnly]: skipped at App.PhoneWidth (640 px) and wider.
    public const string PhoneOnly = "PhoneOnly";

    // [DisposableProject]: creates a project in the target's organization (PlatformTest.CreateDisposableProjectAsync).
    public const string DisposableProject = "DisposableProject";
}
