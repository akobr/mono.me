namespace _42.Platform.Cli.AutomationTests;

// The Cli section: which sform the tests run and how long a run may take. Read with Settings.Bind<CliSettings>("Cli");
// the defaults are in testsettings.json.
public sealed class CliSettings
{
    public const string Section = "Cli";

    public const int DefaultTimeoutMs = 120_000;

    // 42.Platform.Cli.dll (run as "dotnet <dll>") or an sform executable; null runs the build of src/Platform/Cli/src of
    // the tests' configuration, recorded when the tests are built (SformLocator). A relative path is relative to the
    // test assembly's directory.
    public string? Path { get; set; }

    // How long one run of sform may take before its process tree is killed and the test fails with the output so far;
    // a run's SformRunOptions.Timeout wins. The device code sign-in waits within it too.
    public int TimeoutMs { get; set; } = DefaultTimeoutMs;

    // A test's working directory (outside the artifacts, SformHomes) is removed when the test passes and kept when it does
    // not, with the list of its files attached; true keeps every one, e.g. to look at what sform wrote.
    public bool KeepWorkingDirectories { get; set; }
}
