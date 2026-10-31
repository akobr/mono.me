namespace _42.Testing.UI.Automation.Runtime.Settings;

// The settings cannot run the suite. Errors name the settings (Users:Pool:2:Password), never a secret value.
public sealed class UiAutomationSettingsException : Exception
{
    public UiAutomationSettingsException(string error, Exception? innerException = null)
        : this([error], [], innerException)
    {
    }

    public UiAutomationSettingsException(IReadOnlyList<string> errors, IReadOnlyList<string> sources, Exception? innerException = null)
        : base(FormatMessage(errors, sources), innerException)
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }

    private static string FormatMessage(IReadOnlyList<string> errors, IReadOnlyList<string> sources)
    {
        if (errors.Count == 1 && sources.Count == 0)
        {
            return errors[0];
        }

        var lines = new List<string> { "The UI automation settings are not valid:" };
        lines.AddRange(errors.Select(error => $"- {error}"));

        if (sources.Count > 0)
        {
            lines.Add($"Read from: {string.Join(", ", sources)}.");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
