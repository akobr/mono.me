using System.Collections.Generic;
using System.Linq;

namespace _42.Platform.Storyteller.Configuring;

public class InheritanceGraphNode
{
    private readonly List<InheritanceGraphNode> _ancestors = new(2);

    public InheritanceGraphNode(FullKey key)
    {
        Key = key;
    }

    public FullKey Key { get; }

    internal InheritanceGraphNode? Descendant { get; private set; }

    public IEnumerable<InheritanceGraphNode> GetAncestors()
        => _ancestors.OrderBy(ancestor => ancestor.Key.Annotation.Type);

    public override string? ToString()
    {
        return Key.ToString();
    }

    internal void SetDescendant(InheritanceGraphNode descendant)
    {
        if (Descendant is not null)
        {
            Descendant.RemoveAncestor(this);
            Descendant.AddAncestor(descendant);
        }

        descendant.AddAncestor(this);
        Descendant = descendant;
    }

    private void AddAncestor(InheritanceGraphNode ancestor)
    {
        _ancestors.Add(ancestor);
    }

    private void RemoveAncestor(InheritanceGraphNode ancestor)
    {
        _ancestors.Remove(ancestor);
    }
}
