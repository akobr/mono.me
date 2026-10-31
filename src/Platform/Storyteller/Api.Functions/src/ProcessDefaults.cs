namespace _42.Platform.Storyteller.Api;

// Process-wide settings that Program.cs applies first, before anything uses a Regex.
internal static class ProcessDefaults
{
    // The match timeout of every Regex without its own: NJsonSchema's format validators and the match it repeats after
    // the schema check's own (ConfigurationSchemaOptions.PatternMatchTimeout), and the caller's name queries. Infinite
    // unless it is set, and Regex reads it only once per process.
    public static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromMilliseconds(200);

    public static void Apply()
    {
        AppContext.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", RegexMatchTimeout);
    }
}
