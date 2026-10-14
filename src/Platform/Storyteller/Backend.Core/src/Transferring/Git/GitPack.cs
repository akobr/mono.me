namespace _42.Platform.Storyteller.Transferring.Git;

// A packfile and its index; the name of both files is pack-<checksum of the pack>.
public sealed record class GitPack(string Name, byte[] Pack, byte[] Index);
