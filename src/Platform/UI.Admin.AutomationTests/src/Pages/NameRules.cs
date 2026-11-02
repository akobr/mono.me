namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The client's checks of an organization, project or view name (app/forms/names.schema.ts, which mirrors the API's
// NameRules): New organization, New project and New view take their messages from the same schema.
public static class NameRules
{
    // A name against the pattern, the same for every kind of name.
    public const string PatternMessage = "Use 2 to 63 lower-case letters, digits or hyphens, starting with a letter or a digit.";
}
