using TUnit.Core;

namespace _42.Testing.UI.Automation.Runtime.TUnit;

// The small, fast set that gates a pull request: the category Elementary, selected with
// --treenode-filter "/**[Category=Elementary]".
public sealed class ElementaryAttribute() : CategoryAttribute(AutomationCategories.Elementary);
