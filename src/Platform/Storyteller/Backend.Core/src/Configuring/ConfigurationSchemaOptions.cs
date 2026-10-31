namespace _42.Platform.Storyteller.Configuring;

// Limits of reading a configuration schema and of checking a document against it. A schema is written by a project
// member, so neither may hold a worker for long; both limits are wall-clock time on a shared host, hence options.
public class ConfigurationSchemaOptions
{
    public const string SectionName = "ConfigurationSchemas";

    public static readonly TimeSpan MinPatternMatchTimeout = TimeSpan.FromMilliseconds(10);
    public static readonly TimeSpan MaxPatternMatchTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan MinCheckBudget = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxCheckBudget = TimeSpan.FromMinutes(5);

    // How long one match of a 'pattern' or a 'patternProperties' key may take. The API host's process default
    // (REGEX_DEFAULT_MATCH_TIMEOUT) also caps the match that NJsonSchema repeats.
    public TimeSpan PatternMatchTimeout { get; set; } = TimeSpan.FromMilliseconds(200);

    // How long a parse of a schema, and a check of one document against it, may take in total.
    public TimeSpan CheckBudget { get; set; } = TimeSpan.FromSeconds(10);
}
