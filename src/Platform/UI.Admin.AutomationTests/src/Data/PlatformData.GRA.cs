namespace _42.Platform.UI.Admin.AutomationTests.Data;

// The generators of the Graph section: the tree that Bulk create of one execution creates (GraphTree).
public sealed partial class PlatformData
{
    // A new execution exe.{s}.{r}.{c} with generated parts, and the four ancestors Bulk create adds to it: the tree that
    // every graph test draws in focus mode, so its counts are exact on any target.
    public GraphTree GraphTree()
    {
        return new GraphTree(AnnotationKey(Execution));
    }
}
