namespace _42.Testing.UI.Automation.Reporting.Testiny;

// SizeBytes: the size of the file on disk, or the size the report recorded when the file is gone.
internal sealed record TestinyArtifactNote(TestArtifact Artifact, TestinyArtifactState State, long SizeBytes);
