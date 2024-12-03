using System.ComponentModel;
using System.Reflection;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

using IVsTestDiscoverer = Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter.ITestDiscoverer;
using IVsTestExecutor = Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter.ITestExecutor;
using VsTestCase = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestCase;

namespace _42.nHolistic.Runner.VisualStudio;

[FileExtension(".dll")]
[FileExtension(".exe")]
[DefaultExecutorUri(Constants.ExecutorUri)]
[ExtensionUri(Constants.ExecutorUri)]
[Category("managed")]
public class VsTestRunner : IVsTestDiscoverer, IVsTestExecutor
{
    private bool _isCanceled;
    private IHost? _host;

    public void DiscoverTests(
        IEnumerable<string> sources,
        IDiscoveryContext discoveryContext,
        IMessageLogger logger,
        ITestCaseDiscoverySink discoverySink)
    {
        var thisAssembly = typeof(VsTestRunner).Assembly;
        WriteRuntimeVersion(logger, thisAssembly);

        var builder = new HostBuilder();
        builder.ConfigureServices((context, services) =>
        {
            // nHolistic services
            services.AddSingleton<ISourcesProvider>(new SourcesProvider(sources));
            services.AddSingleton<ITestCaseDiscoverySink>(discoverySink);
            services.AddSingleton<IMessageLogger>(logger);
            services.AddSingleton<ITestDiscoverer, TestDiscoverer>();
            services.AddSingleton<IVisualStudioTestDiscoverer, VisualStudioTestDiscoverer>();
            services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<IVisualStudioTestDiscoverer>());

            // MediatR registrations and overrides
            services.AddMediatR(config => config.RegisterServicesFromAssembly(thisAssembly));
            services.AddSingleton<LogNotificationHandler>();
            services.AddSingleton<TestCaseNotificationHandler>();
            services.Replace(ServiceDescriptor.Singleton<INotificationHandler<LogNotification>>(
                provider => provider.GetRequiredService<LogNotificationHandler>()));
            services.Replace(ServiceDescriptor.Singleton<INotificationHandler<TestCaseDiscoveredNotification>>(
                provider => provider.GetRequiredService<TestCaseNotificationHandler>()));
        });

        _host = builder.Build();
        _host.Run();
    }

    public void RunTests(
        IEnumerable<VsTestCase>? tests,
        IRunContext? runContext,
        IFrameworkHandle? frameworkHandle)
    {
        var thisAssembly = typeof(VsTestRunner).Assembly;
        WriteRuntimeVersion(frameworkHandle, thisAssembly);

        if (tests is null)
        {
            const string errorMessage = "No test cases or sources have been provided to runner!";

            frameworkHandle?.SendMessage(
                TestMessageLevel.Error,
                $"[{Constants.RunnerName} 00:00.00] {errorMessage}");

            throw new InvalidOperationException(errorMessage);
        }

        var builder = new HostBuilder();
        builder.ConfigureServices((context, services) =>
        {
            services.AddSingleton<ITestCasesProvider>(new StaticTestCasesProvider(tests));

            if (runContext is not null)
            {
                services.AddSingleton<IRunContext>(runContext);
            }

            if (frameworkHandle is not null)
            {
                services.AddSingleton<IFrameworkHandle>(frameworkHandle);
            }

            // nHolistic services
            services.AddSingleton<ITestExecutor, TestExecutor>();
            services.AddSingleton<IVisualStudioTestExecutor, VisualStudioTestExecutor>();
            services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<IVisualStudioTestExecutor>());

            // MediatR registrations and overrides
            services.AddMediatR(config => config.RegisterServicesFromAssembly(typeof(VsTestRunner).Assembly));
            services.AddSingleton<LogNotificationHandler>();
            services.Replace(ServiceDescriptor.Singleton<INotificationHandler<LogNotification>>(
                provider => provider.GetRequiredService<LogNotificationHandler>()));
        });

        _host = builder.Build();
        _host.Run();
    }

    public void RunTests(
        IEnumerable<string>? sources,
        IRunContext? runContext,
        IFrameworkHandle? frameworkHandle)
    {
        var thisAssembly = typeof(VsTestRunner).Assembly;
        WriteRuntimeVersion(frameworkHandle, thisAssembly);

        if (sources is null)
        {
            const string errorMessage = "No test cases or sources have been provided to runner!";

            frameworkHandle?.SendMessage(
                TestMessageLevel.Error,
                $"[{Constants.RunnerName} 00:00.00] {errorMessage}");

            throw new InvalidOperationException(errorMessage);
        }

        var builder = new HostBuilder();
        builder.ConfigureServices((context, services) =>
        {
            services.AddSingleton<ITestCasesProvider, TestCasesProviderFromDiscoverer>();
            services.AddSingleton<ISourcesProvider>(new SourcesProvider(sources));

            if (runContext is not null)
            {
                services.AddSingleton<IRunContext>(runContext);
            }

            if (frameworkHandle is not null)
            {
                services.AddSingleton<IFrameworkHandle>(frameworkHandle);
            }

            // nHolistic services
            services.AddSingleton<ITestExecutor, TestExecutor>();
            services.AddSingleton<IVisualStudioTestDiscoverer, VisualStudioTestDiscoverer>();
            services.AddSingleton<TestCasesProviderFromDiscoverer>();
            services.AddSingleton<ITestCasesProvider>(provider => provider.GetRequiredService<TestCasesProviderFromDiscoverer>());
            services.AddSingleton<ITestCasesRegister>(provider => provider.GetRequiredService<TestCasesProviderFromDiscoverer>());
            services.AddSingleton<IVisualStudioTestExecutor, VisualStudioTestExecutor>();
            services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<IVisualStudioTestDiscoverer>());
            services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<IVisualStudioTestExecutor>());

            // MediatR registrations and overrides
            services.AddMediatR(config => config.RegisterServicesFromAssembly(typeof(VsTestRunner).Assembly));
            services.AddSingleton<LogNotificationHandler>();
            services.AddSingleton<TestCaseNotificationForRunnerHandler>();
            services.Replace(ServiceDescriptor.Singleton<INotificationHandler<LogNotification>>(
                provider => provider.GetRequiredService<LogNotificationHandler>()));
            services.Replace(ServiceDescriptor.Singleton<INotificationHandler<TestCaseDiscoveredNotification>>(
                provider => provider.GetRequiredService<TestCaseNotificationForRunnerHandler>()));
        });

        _host = builder.Build();
        _host.Run();
    }

    public void Cancel()
    {
        if (_isCanceled)
        {
            return;
        }

        _isCanceled = true;

        if (_host is null)
        {
            return;
        }

        var publisher = _host.Services.GetRequiredService<IPublisher>();
        publisher.Publish(new LogNotification { Message = "The cancellation requested." });
        _host?.StopAsync();
    }

    private static void WriteRuntimeVersion(IMessageLogger? logger, Assembly assembly)
    {
        logger?.SendMessage(TestMessageLevel.Informational,
            $"[{Constants.RunnerName} 00:00.00] {assembly.GetVersionInfo()}");
    }
}
