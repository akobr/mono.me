using System.IO.Abstractions;
using _42.Testing.UI.Automation.Reporting.Testiny.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace _42.Testing.UI.Automation.Reporting.Testiny;

// Creates the Testiny reporter of a run from the Reporting:Testiny section; the consumer registers Instance with its
// setup. Validation fails with every problem at once (ApiKey and ProjectId first of all). From the services it takes,
// when there: IHttpClientFactory (the named client "Testiny"; else an own HttpClient that the reporter disposes),
// IFileSystem for the artifacts, ILoggerFactory, and the run's IConfiguration, whose Reporting:UnmappedTests turns the
// check of the linked ids on when it is not Ignore.
public sealed class TestinyReporterProvider : ITestRunReporterProvider
{
    public const string HttpClientName = "Testiny";

    public static TestinyReporterProvider Instance { get; } = new();

    public string System => TestinyTestRunReporter.SystemName;

    // The options of the section; throws InvalidOperationException with every validation error.
    public static TestinyOptions BindOptions(IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(section);

        var options = new TestinyOptions();
        section.Bind(options);

        var errors = options.Validate((section as IConfigurationSection)?.Path ?? TestinyOptions.SectionName);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        return options;
    }

    public ITestRunReporter Create(IServiceProvider services, IConfiguration section, TestRunDescriptor run)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(run);

        var options = BindOptions(section);
        var validateCaseIds = ValidatesCaseIds(services, section);
        var fileSystem = services.GetService(typeof(IFileSystem)) as IFileSystem;
        var logger = (services.GetService(typeof(ILoggerFactory)) as ILoggerFactory)?.CreateLogger<TestinyTestRunReporter>();

        if (services.GetService(typeof(IHttpClientFactory)) is IHttpClientFactory factory)
        {
            var shared = factory.CreateClient(HttpClientName);
            return new TestinyTestRunReporter(new TestinyClient(shared, options), options, validateCaseIds, fileSystem, logger);
        }

        // The client's own timeout is RequestTimeoutSeconds per request; connections are renewed for DNS changes.
        var owned = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = Timeout.InfiniteTimeSpan };
        return new TestinyTestRunReporter(new TestinyClient(owned, options), options, validateCaseIds, fileSystem, logger, owned);
    }

    // Reporting:UnmappedTests (the parent of the section) Warn or Fail; Ignore, the default, saves the request.
    private static bool ValidatesCaseIds(IServiceProvider services, IConfiguration section)
    {
        if (services.GetService(typeof(IConfiguration)) is not IConfiguration configuration)
        {
            return false;
        }

        var parent = ConfigurationPath.GetParentPath(section is IConfigurationSection { Path: { } path } ? path : TestinyOptions.SectionName);
        var policy = configuration[string.IsNullOrEmpty(parent) ? "UnmappedTests" : ConfigurationPath.Combine(parent, "UnmappedTests")]?.Trim();
        return !string.IsNullOrEmpty(policy) && !string.Equals(policy, "Ignore", StringComparison.OrdinalIgnoreCase);
    }
}
