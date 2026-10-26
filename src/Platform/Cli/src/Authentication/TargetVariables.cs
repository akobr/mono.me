namespace _42.Platform.Cli.Authentication;

// The command target of sform, for users and machines alike; not credential variables. The credential names are the
// constants of the SDK (StorytellerVariables), and so is STORYTELLER_KEY, which also gives a machine its target.
public static class TargetVariables
{
    public const string PROJECT = "STORYTELLER_PROJECT";
    public const string VIEW = "STORYTELLER_VIEW";
}
