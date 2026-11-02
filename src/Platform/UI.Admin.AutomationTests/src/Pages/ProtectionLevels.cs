namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The protection levels of a secret value by their labels in the admin UI (Public, Standard, HSM, BYOK, HYOK) and by
// their API names (Public, Standard, Hsm, Byok, Hyok), which the URL's ?level and the API's messages use. The page
// objects take either.
public static class ProtectionLevels
{
    // Every level, Public first: its label and its API name.
    private static readonly (string Label, string ApiName)[] Levels =
    [
        ("Public", "Public"),
        ("Standard", "Standard"),
        ("HSM", "Hsm"),
        ("BYOK", "Byok"),
        ("HYOK", "Hyok"),
    ];

    // The label of a level ("HSM"), by its label or its API name, in any case.
    public static string Label(string levelOrApiName)
    {
        return Find(levelOrApiName).Label;
    }

    // The API name of a level ("Hsm"), by its label or its API name, in any case.
    public static string ApiName(string levelOrApiName)
    {
        return Find(levelOrApiName).ApiName;
    }

    private static (string Label, string ApiName) Find(string level)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(level);

        foreach (var known in Levels)
        {
            if (string.Equals(known.Label, level, StringComparison.OrdinalIgnoreCase) || string.Equals(known.ApiName, level, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        throw new ArgumentException(
            $"'{level}' is not a protection level; use one of {string.Join(", ", Levels.Select(known => known.Label))} (or the API names {string.Join(", ", Levels.Select(known => known.ApiName))}).",
            nameof(level));
    }
}
