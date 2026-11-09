namespace _42.Platform.Cli.Configuration;

public class LoggingOptions
{
    // Relative to the state directory (CliPaths.GetLogFilePath): next to the assembly, or in SFORM_HOME.
    public string LogFilePath { get; set; } = CliPaths.DEFAULT_LOG_FILE_PATH;

    public string? SentryDsn { get; set; }
}
