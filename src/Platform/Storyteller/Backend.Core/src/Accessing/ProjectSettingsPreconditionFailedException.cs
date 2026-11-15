namespace _42.Platform.Storyteller.Accessing;

// A settings write whose If-Match names another version (412 SettingsPreconditionFailed); the current version goes into
// the Details of the API's answer, so a client can tell how far behind it is.
public sealed class ProjectSettingsPreconditionFailedException(long currentVersion)
    : PreconditionFailedException(
        $"The project settings changed meanwhile; they are at version {currentVersion}.",
        ErrorCodes.SettingsPreconditionFailed)
{
    public long CurrentVersion { get; } = currentVersion;
}
