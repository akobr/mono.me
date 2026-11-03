namespace _42.Platform.Storyteller.Sdk.Configuring.Settings;

// The STORYTELLER_* names of the provider alone. STORYTELLER_ENDPOINT (with the deprecated STORYTELLER_API_URL) and
// STORYTELLER_KEY are the SDK's StorytellerVariables; the credential variables are read by
// StorytellerCredential.FromEnvironment.
internal static class ConfigurationVariables
{
    public const string MODE = "STORYTELLER_MODE";
    public const string REFRESH_INTERVAL = "STORYTELLER_REFRESH_INTERVAL";
    public const string OPTIONAL = "STORYTELLER_OPTIONAL";
    public const string CACHE_DIR = "STORYTELLER_CACHE_DIR";
}
