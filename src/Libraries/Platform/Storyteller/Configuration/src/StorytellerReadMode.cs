namespace _42.Platform.Storyteller.Sdk.Configuring;

// Which read of the configuration the provider makes: STORYTELLER_MODE, resolved or effective.
public enum StorytellerReadMode
{
    // GET …/configuration/{key}/resolved: bindings evaluated, secrets included when the caller may read them.
    Resolved = 0,

    // GET …/configuration/{key}: the inherited document with its bindings still written as @… expressions.
    Effective,
}
