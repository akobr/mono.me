namespace _42.Testing.UI.Automation;

// The category names every engine adapter maps its attributes to, so a filter such as
// --treenode-filter "/**[Category=Elementary]" means the same in every project.
public static class AutomationCategories
{
    // The small, fast set that gates a pull request.
    public const string Elementary = "Elementary";

    // Tests that run alone, after the parallel ones, and restore what they change.
    public const string Serial = "Serial";

    // Housekeeping that is skipped unless asked for, e.g. purging generated data or replaying a results file.
    public const string Maintenance = "Maintenance";

    // The prefix of an area category: [Area("Annotations")] is the category "Area-Annotations".
    public const string AreaPrefix = "Area-";

    public static string Area(string area)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(area);
        return AreaPrefix + area;
    }
}
