namespace _42.Platform.Cli.AutomationTests.Running;

// A stream of a run of sform. In a pseudo terminal both are the one screen, and a check of any stream looks at it.
public enum SformStream
{
    StdOut,
    StdErr,

    // The screen of a run in a pseudo terminal; for a redirected run, both streams in the order their lines arrived.
    Terminal,
}
