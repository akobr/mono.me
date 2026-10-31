using Microsoft.Extensions.Configuration;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;

internal sealed class FakeReporterProvider(string system = "Fake") : ITestRunReporterProvider
{
    public string System { get; } = system;

    public List<FakeReporter> Created { get; } = [];

    public IConfiguration? Section { get; private set; }

    public IServiceProvider? Services { get; private set; }

    public ITestRunReporter Create(IServiceProvider services, IConfiguration section, TestRunDescriptor run)
    {
        Section = section;
        Services = services;
        if (string.IsNullOrEmpty(section["ApiKey"]))
        {
            throw new InvalidOperationException("ApiKey is required.");
        }

        var reporter = new FakeReporter(System);
        Created.Add(reporter);
        return reporter;
    }
}
