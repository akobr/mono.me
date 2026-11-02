namespace _42.Platform.Storyteller.DbCreator.Configuration;

// Seeds the fixed accounts of the AuthKit staging environment (the default staging user as a second owner, and the
// ui-tests organization with its UI test users) when stagingSeed:enabled is true (or the environment variable
// stagingSeed__enabled). Off by default, so a run against a deployed database never grants them access; the Aspire
// host turns it on for its local emulator.
public class StagingSeedOptions
{
    public const string SECTION = "stagingSeed";

    public bool Enabled { get; set; }
}
