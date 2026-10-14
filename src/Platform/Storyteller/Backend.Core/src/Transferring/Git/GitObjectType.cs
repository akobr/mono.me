namespace _42.Platform.Storyteller.Transferring.Git;

// Object types as numbered in a packfile.
public enum GitObjectType
{
    Commit = 1,
    Tree = 2,
    Blob = 3,
    OfsDelta = 6,
}
