namespace _42.Platform.Storyteller.Configuring;

public static class InheritanceGraphNodeExtensions
{
    public static InheritanceGraphNode SetDescendant(this InheritanceGraphNode @this, FullKey key)
    {
        var descendant = new InheritanceGraphNode(key);
        @this.SetDescendant(descendant);
        return descendant;
    }

    public static InheritanceGraphNode CreateAncestor(this InheritanceGraphNode @this, FullKey key)
    {
        var ancestor = new InheritanceGraphNode(key);
        ancestor.SetDescendant(@this);
        return ancestor;
    }
}
