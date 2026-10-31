using TUnit.Core;

namespace _42.Testing.UI.Automation.Runtime.TUnit;

// The functional area of a test as the category "Area-<area>": [Area("Annotations")] is selected with
// --treenode-filter "/**[Category=Area-Annotations]". Not sealed, so a project can wrap an enum of its areas.
public class AreaAttribute(string area) : CategoryAttribute(AutomationCategories.Area(area))
{
    public string Area { get; } = area;
}
