using System.Text;

namespace _42.Testing.UI.Automation.Runtime.Reporting;

// The default Reporting:RunDescription: a markdown table of the run's properties, which never hold a secret.
public static class RunDescriptions
{
    private static readonly (string Key, string Label)[] Rows =
    [
        (TestRunProperties.Target, "Target"),
        (TestRunProperties.Profile, "Profile"),
        (TestRunProperties.Provider, "Provider"),
        (TestRunProperties.Engine, "Engine"),
        (TestRunProperties.Channel, "Channel"),
        (TestRunProperties.BrowserVersion, "Browser version"),
        (TestRunProperties.Device, "Device"),
        (TestRunProperties.Seed, "Seed"),
        (TestRunProperties.Branch, "Branch"),
        (TestRunProperties.Sha, "Commit"),
        (TestRunProperties.BuildUrl, "Build"),
        (TestRunProperties.Machine, "Machine"),
        (TestRunProperties.TestEngine, "Test engine"),
        (TestRunProperties.PlaywrightVersion, "Playwright"),
        (TestRunProperties.RunId, "Run id"),
    ];

    public static string Table(IReadOnlyDictionary<string, string> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        var builder = new StringBuilder();
        builder.Append("| Setting | Value |\n|---|---|\n");
        foreach (var (key, label) in Rows)
        {
            if (properties.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                builder.Append("| ").Append(label).Append(" | ").Append(value.Replace("|", "\\|", StringComparison.Ordinal)).Append(" |\n");
            }
        }

        return builder.ToString();
    }
}
