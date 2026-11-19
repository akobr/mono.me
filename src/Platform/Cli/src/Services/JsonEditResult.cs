using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Services;

/// <summary>
/// The outcome of an interactive JSON edit.
/// </summary>
/// <param name="Edited">The confirmed edited document; null when nothing should be saved.</param>
/// <param name="ExitCode">The exit code to return when <paramref name="Edited"/> is null.</param>
/// <param name="Rejected">
/// The text of the last edit that was refused (not JSON, or refused by the check), when the edit ended after it without a
/// document: the caller may keep it, as the temporary file is deleted.
/// </param>
public record class JsonEditResult(JObject? Edited, int ExitCode, string? Rejected = null);
