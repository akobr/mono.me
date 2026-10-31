namespace _42.Testing.UI.Automation;

// A file a test produced (screenshot, trace, video, console log); Path is absolute. A reporter uploads what it wants.
public sealed record TestArtifact(string Name, string Path, string MimeType, ArtifactKind Kind, long SizeBytes);
