namespace _42.Platform.Cli.Configuration;

// The environment variables of sform itself. The sign-in of a machine reads the STORYTELLER_* variables of the SDK
// instead (StorytellerVariables), telemetry DO_NOT_TRACK and SFORM_TELEMETRY_OPTOUT (TelemetrySettings).
public static class SformVariables
{
    // An absolute directory that holds every file sform reads or writes, in place of the folder of the assembly and
    // the user's .42for.net folder (CliPaths).
    public const string HOME = "SFORM_HOME";

    // "file": the AuthKit session and the MSAL cache are kept unencrypted in SFORM_HOME; honoured only with SFORM_HOME.
    public const string TOKEN_STORE = "SFORM_TOKEN_STORE";

    // "json": the device code prompt is also written as one JSON line on standard error.
    public const string DEVICE_CODE_OUTPUT = "SFORM_DEVICE_CODE_OUTPUT";

    // The program that `sform account --browser` opens the sign-in page with.
    public const string BROWSER = "SFORM_BROWSER";

    // The conventional variable of the same meaning, used when SFORM_BROWSER is not set: a list of commands separated by
    // the path separator, each a program or a command line with %s for the address.
    public const string CONVENTIONAL_BROWSER = "BROWSER";

    // The prefix of the configuration variables: SFORM_<section>__<key>, e.g. SFORM_general__baseUrl (Startup).
    public const string CONFIGURATION_PREFIX = "SFORM_";

    // The environment of the host; honoured by a build from source (Debug), which runs as Development without it.
    public const string DOTNET_ENVIRONMENT = "DOTNET_ENVIRONMENT";

    // The width of the output when sform has no console at all (SformConsole).
    public const string COLUMNS = "COLUMNS";

    // The values.
    public const string TOKEN_STORE_FILE = "file";
    public const string DEVICE_CODE_OUTPUT_JSON = "json";
}
