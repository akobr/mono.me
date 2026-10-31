using _42.Testing.UI.Automation.Runtime.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace _42.Testing.UI.Automation.Runtime.Reporting;

// The reporters of a run: none unless Reporting:Enabled; then the providers named by Reporting:Systems, or every
// registered provider whose Reporting:<System> section exists. A configuration that selects nothing, names an
// unregistered system or that a provider refuses fails the start, because reporting was asked for explicitly; the
// reporters created before the failure are disposed. The services a provider gets answer IConfiguration with the run's
// configuration and, unless the consumer has one, ILoggerFactory with the session's logger (ReporterServices).
internal static class ReporterSelection
{
    public static async Task<IReadOnlyList<ITestRunReporter>> CreateAsync(
        UiAutomationSettings settings,
        IReadOnlyList<ITestRunReporterProvider> providers,
        IServiceProvider services,
        TestRunDescriptor run,
        ILogger logger)
    {
        if (!settings.Reporting.Enabled)
        {
            return [];
        }

        var reporting = settings.Configuration.GetSection("Reporting");
        var registered = string.Join(", ", providers.Select(provider => provider.System));
        var systems = settings.Reporting.Systems.Count > 0
            ? settings.Reporting.Systems.Select(system => system.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : providers.Where(provider => reporting.GetSection(provider.System).Exists()).Select(provider => provider.System).ToArray();

        if (systems.Length == 0)
        {
            throw new UiAutomationSettingsException(providers.Count == 0
                ? "Reporting:Enabled is true, but the setup registers no reporter provider."
                : $"Reporting:Enabled is true, but no reporter is selected; set Reporting:Systems or add a Reporting:<System> section for one of: {registered}.");
        }

        var reporterServices = new ReporterServices(services, settings.Configuration, logger);
        var reporters = new List<ITestRunReporter>();
        try
        {
            foreach (var system in systems)
            {
                var provider = providers.FirstOrDefault(candidate => string.Equals(candidate.System, system, StringComparison.OrdinalIgnoreCase))
                    ?? throw new UiAutomationSettingsException(
                        $"Reporting:Systems names '{system}', but no reporter provider is registered for it; registered: {(registered.Length == 0 ? "none" : registered)}.");

                try
                {
                    reporters.Add(provider.Create(reporterServices, reporting.GetSection(provider.System), run));
                }
                catch (Exception exception) when (exception is not UiAutomationSettingsException)
                {
                    throw new UiAutomationSettingsException($"Reporting:{provider.System}: {exception.Message}", exception);
                }
            }
        }
        catch
        {
            await DisposeAsync(reporters);
            throw;
        }

        return reporters;
    }

    // Disposes reporters that never started a run; a reporter that fails to dispose does not hide the original error.
    public static async Task DisposeAsync(IEnumerable<ITestRunReporter> reporters)
    {
        foreach (var reporter in reporters)
        {
            try
            {
                await reporter.DisposeAsync();
            }
            catch (Exception)
            {
                // The start fails anyway; its error is the one that counts.
            }
        }
    }
}
