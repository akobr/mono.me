namespace _42.Platform.Cli.AutomationTests.Access;

// Whose SFORM_HOME it is (SformHome).
public enum SformHomeKind
{
    // A pool user's, for the session, signed in once and reused by the user's tests.
    User,

    // An empty folder of one test: [FreshSignIn], CliTest.FreshHome().
    Fresh,

    // An empty folder of one test for a machine's runs: no user state at all.
    Machine,
}
