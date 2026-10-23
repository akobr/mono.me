namespace _42.Platform.Storyteller.Sdk.Configuring.Settings;

// The STORYTELLER_* names the provider reads itself; the credential variables are read by
// StorytellerCredential.FromEnvironment.
internal static class ConfigurationVariables
{
    public const string ENDPOINT = "STORYTELLER_ENDPOINT";

    // The sform name of the endpoint, accepted as a fallback.
    public const string API_URL = "STORYTELLER_API_URL";

    public const string KEY = "STORYTELLER_KEY";
    public const string MODE = "STORYTELLER_MODE";
    public const string REFRESH_INTERVAL = "STORYTELLER_REFRESH_INTERVAL";
    public const string OPTIONAL = "STORYTELLER_OPTIONAL";
    public const string CACHE_DIR = "STORYTELLER_CACHE_DIR";
}
