namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

// An entry of a tree: a file (100644) or a subtree (40000), with the id of its blob or tree.
internal sealed record class GitTestTreeEntry(string Mode, string Name, string Id)
{
    public bool IsTree => Mode == "40000";
}
