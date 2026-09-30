using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Services;

/// <summary>
/// The outcome of an interactive JSON edit.
/// </summary>
/// <param name="Edited">The confirmed edited document; null when nothing should be saved.</param>
/// <param name="ExitCode">The exit code to return when <paramref name="Edited"/> is null.</param>
public record class JsonEditResult(JObject? Edited, int ExitCode);
