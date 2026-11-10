namespace _42.Platform.Cli.Commands.Storyteller;

// The annotation type at the root of the tree of sform story --tree (--tree-root).
public enum StoryTreeRoot
{
    // subject → context → responsibility; the default.
    Subject = 0,

    // responsibility → subject → context.
    Responsibility,
}
