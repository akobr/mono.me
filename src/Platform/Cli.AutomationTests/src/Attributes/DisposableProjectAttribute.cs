using TUnit.Core;

// In the root namespace like AreaAttribute, so a test writes [DisposableProject] without a using.
namespace _42.Platform.Cli.AutomationTests;

// A test that changes project-wide state in a project of its own (mode P, CliTest.CreateDisposableProjectAsync, as an
// Owner of Target:Organization through sform account points create in a pseudo terminal): the category
// DisposableProject. The projects stay in the organization, so a run can leave these tests out with
// --treenode-filter "/**[Category!=DisposableProject]".
public sealed class DisposableProjectAttribute() : CategoryAttribute(CliCategories.DisposableProject);
