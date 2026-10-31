namespace _42.Testing.UI.Automation.Runtime.Settings;

// Execution:Trace, Execution:Screenshot and Execution:Video: never, kept for a failed test only, or kept for every test.
public enum ArtifactPolicy
{
    Off,
    OnFailure,
    Always,
}
