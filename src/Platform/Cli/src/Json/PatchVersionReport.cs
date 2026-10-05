namespace _42.Platform.Cli.Json;

/// <summary>
/// The version line printed after a configuration patch.
/// </summary>
/// <param name="Message">The line to print.</param>
/// <param name="Changed">Whether the server returned a different version.</param>
public readonly record struct PatchVersionReport(string Message, bool Changed)
{
    /// <summary>
    /// Builds the version line from the version read before the call and the version in the response.
    /// </summary>
    public static PatchVersionReport Create(string annotationKey, long versionBefore, long versionAfter)
    {
        if (versionAfter == versionBefore)
        {
            return new PatchVersionReport(
                $"Patch did not change the stored content of '{annotationKey}'. Still version {versionAfter}.",
                false);
        }

        return new PatchVersionReport(
            $"Configuration for '{annotationKey}' patched: version {versionBefore} -> {versionAfter}.",
            true);
    }
}
