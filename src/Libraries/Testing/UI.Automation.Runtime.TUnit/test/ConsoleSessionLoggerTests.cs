using _42.Testing.UI.Automation.Runtime.Settings;
using Microsoft.Extensions.Logging;

namespace _42.Testing.UI.Automation.Runtime.TUnit.UnitTests;

public sealed class ConsoleSessionLoggerTests
{
    [Fact]
    public void Format_ShowsTheMessageOfAWarning_AndTheWholeExceptionOfAnError()
    {
        var exception = new InvalidOperationException("Boom", new TimeoutException("Inner"));

        ConsoleSessionLogger.Format(LogLevel.Information, "Started.", null).ShouldBe("Started.");
        ConsoleSessionLogger.Format(LogLevel.Warning, "No screenshot.", exception).ShouldBe("warning: No screenshot. (Boom)");
        var error = ConsoleSessionLogger.Format(LogLevel.Error, "The session did not start.", exception);
        error.ShouldStartWith($"error: The session did not start.{Environment.NewLine}System.InvalidOperationException: Boom");
        error.ShouldContain("Inner");
    }

    // An error prints the whole exception, and a Playwright call log in it keeps what a fill typed.
    [Fact]
    public void Lines_ShowTheSecretsOfTheLoadedSettings_AsStars()
    {
        var settings = new UiAutomationSettings
        {
            Users = new UsersSettings { Pool = [new PoolUserSettings { Email = "ada@example.test", Password = "pw-123" }] },
            BrowserStack = new BrowserStackSettings { AccessKey = "bs-key-456" },
        };
        var logger = ConsoleSessionLogger.Instance;
        var text = ConsoleSessionLogger.Format(
            LogLevel.Error,
            "The UI automation session did not start: fill(\"pw-123\")",
            new InvalidOperationException("Call log:\n  -   fill(\"pw-123\") with bs-key-456"));

        try
        {
            logger.Redact = SettingsRedaction.CreateRedactor(settings);

            var lines = logger.Lines(text);

            lines[0].ShouldBe("error: The UI automation session did not start: fill(\"***\")");
            lines.ShouldContain("  -   fill(\"***\") with ***");
            string.Join('\n', lines).ShouldNotContain("pw-123");
            string.Join('\n', lines).ShouldNotContain("bs-key-456");
        }
        finally
        {
            logger.Redact = null;
        }

        logger.Lines("pw-123").ShouldBe(["pw-123"]);
    }
}
