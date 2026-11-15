namespace _42.Platform.Storyteller.Accessing;

// The settings after a write, and whether the write changed them (false: nothing was written, the version stayed).
public sealed record class ProjectSettingsWrite(ProjectSettings Settings, bool Changed);
