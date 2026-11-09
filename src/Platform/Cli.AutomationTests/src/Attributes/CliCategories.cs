// In the root namespace like the attributes that add these categories.
namespace _42.Platform.Cli.AutomationTests;

// The CLI suite's TUnit categories next to the library's Elementary, Serial and Area-<area>; a run selects or leaves them
// out with --treenode-filter "/**[Category!=DisposableProject]".
public static class CliCategories
{
    // [MachineOnly]: runs as a machine only, leases no pool user (mode M).
    public const string MachineOnly = "MachineOnly";

    // [FreshSignIn]: works in an empty SFORM_HOME of its own and signs in itself (mode F).
    public const string FreshSignIn = "FreshSignIn";

    // [Terminal]: runs sform in a pseudo terminal (mode T); skipped where none is supported.
    public const string Terminal = "Terminal";

    // [DisposableProject]: creates a project in the target's organization (mode P).
    public const string DisposableProject = "DisposableProject";

    // [RequiresFeature("<Feature>")]: "Requires-<Feature>", skipped unless Target:Features lists it (mode X).
    public const string RequiresPrefix = "Requires-";

    // The self tests of the suite's own machinery, which need no target (SelfTests/).
    public const string SelfTest = "SelfTest";

    // The checks of the coverage of sform's command tree and of the catalogue (Coverage/), which need no target.
    public const string Coverage = "Coverage";
}
