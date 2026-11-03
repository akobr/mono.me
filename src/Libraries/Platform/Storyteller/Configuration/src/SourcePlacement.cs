namespace _42.Platform.Storyteller.Sdk.Configuring;

// Where AddStoryteller puts the source among the sources of the builder.
public enum SourcePlacement
{
    // In front of the last unprefixed environment-variables source: appsettings*.json < Storyteller < environment
    // variables < command line. Appended when the builder has no such source.
    BeforeEnvironmentVariables = 0,

    // Appended: nothing added before overrides the platform (strict mode).
    Last,
}
