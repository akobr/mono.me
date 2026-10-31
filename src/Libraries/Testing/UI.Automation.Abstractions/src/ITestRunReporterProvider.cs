using Microsoft.Extensions.Configuration;

namespace _42.Testing.UI.Automation;

// Creates the reporter of one system. Registered by the consuming project (no reflection scanning); the runtime calls it
// when Reporting:Enabled is true and the system is selected. Create validates the section and throws with a message
// that names the missing or wrong setting.
public interface ITestRunReporterProvider
{
    // Matches Reporting:<System> in the configuration and the names in Reporting:Systems.
    string System { get; }

    // section: the Reporting:<System> section.
    ITestRunReporter Create(IServiceProvider services, IConfiguration section, TestRunDescriptor run);
}
