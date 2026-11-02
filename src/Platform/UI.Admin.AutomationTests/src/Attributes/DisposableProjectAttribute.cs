using TUnit.Core;

// In the root namespace like AreaAttribute, so a test writes [DisposableProject] without a using.
namespace _42.Platform.UI.Admin.AutomationTests;

// A test that changes project-wide state in a project of its own (PlatformTest.CreateDisposableProjectAsync, as an
// Owner of Target:Organization): the category DisposableProject. The projects stay in the organization, so a run can
// leave these tests out with --treenode-filter "/**[Category!=DisposableProject]".
public sealed class DisposableProjectAttribute() : CategoryAttribute(PlatformCategories.DisposableProject);
