using System.ComponentModel;
using System.Reflection;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
            services.AddHostedService<VisualStudioTestDiscoverer>();

            // MediatR registrations and overrides
            services.AddMediatR(config => config.RegisterServicesFromAssembly(thisAssembly));
            services.RemoveAll<INotificationHandler<LogNotification>>();
            services.RemoveAll<INotificationHandler<TestCaseDiscoveredNotification>>();
            services.AddSingleton<LogNotificationHandler>();
            services.AddSingleton<TestCaseNotificationHandler>();
            services.AddSingletonFromOtherService<INotificationHandler<LogNotification>, LogNotificationHandler>();
            services.AddSingletonFromOtherService<INotificationHandler<TestCaseDiscoveredNotification>, TestCaseNotificationHandler>();
        });

        _host = builder.Build();
        _host.Run();
        logger.SendMessage(TestMessageLevel.Informational, $"{Constants.RunnerName} is finished.");
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

        if (frameworkHandle is null)
        {
            const string errorMessage = "An instance of IFrameworkHandle is missing.";

            frameworkHandle?.SendMessage(
                TestMessageLevel.Error,
                $"[{Constants.RunnerName} 00:00.00] {errorMessage}");

            throw new InvalidOperationException(errorMessage);
        }

        var testList = tests.ToList();
        frameworkHandle?.SendMessage(
            TestMessageLevel.Error,
            $"[{Constants.RunnerName} 00:00.00] Number of test cases to run: {testList.Count}");

        try
        {
            var builder = new HostBuilder();
            builder.ConfigureServices((context, services) =>
            {
                services.AddSingleton<ITestCasesProvider>(new StaticTestCasesProvider(testList));
                services.AddSingleton<IFrameworkHandle>(frameworkHandle!);
                services.AddSingletonFromOtherService<IMessageLogger, IFrameworkHandle>();
                services.AddSingletonFromOtherService<ITestExecutionRecorder, IFrameworkHandle>();

                if (runContext is not null)
                {
                    services.AddSingleton<IRunContext>(runContext);
                    services.AddSingletonFromOtherService<IDiscoveryContext, IRunContext>();
                }

                // nHolistic services
                services.AddSingleton<ITestRunScopeFactory, TestRunScopeFactory>();
                services.AddSingleton<ITestRunContextProvider, TestRunContextProvider>();
                services.AddScoped<ITestRunContext>(provider => provider.GetRequiredService<ITestRunContextProvider>().GetContext());
                services.AddSingleton<IProxyFactory, ProxyFactory>();
                services.AddSingleton<ISynchronizationService, SynchronizationService>();
                services.AddSingleton<IFixtureStorage, FixtureStorage>();
                services.AddSingletonFromOtherService<IFixtureProvider, IFixtureStorage>();
                services.AddSingleton<Lazy<IFixtureProvider>>(
                    provider => new Lazy<IFixtureProvider>(provider.GetRequiredService<IFixtureStorage>));
                services.AddSingleton<IFixturesProcessingService, FixturesProcessingService>();
                services.AddSingleton<ITypeActivator, TypeActivator>();
                services.AddSingleton<IRunDirectoryProvider, VisualStudioRunDirectoryProvider>();
                services.AddSingleton<ITestCasesMapper, TestCasesMapper>();
                services.AddSingleton<IResultAttachmentsService, ResultAttachmentsService>();
                services.AddSingleton<IExecutionContextBuilder, ExecutionContextBuilder>();
                services.AddSingleton<ITestExecutor, TestExecutor>();
                services.AddHostedService<VisualStudioTestExecutor>();

                // MediatR registrations and overrides
                services.AddMediatR(config => config.RegisterServicesFromAssembly(typeof(VsTestRunner).Assembly));
                services.RemoveAll<INotificationHandler<LogNotification>>();
                services.AddSingleton<LogNotificationHandler>();
                services.AddSingletonFromOtherService<INotificationHandler<LogNotification>, LogNotificationHandler>();
            });

            frameworkHandle?.SendMessage(TestMessageLevel.Informational, $"{Constants.RunnerName} is about to build the host.");
            _host = builder.Build();
            frameworkHandle?.SendMessage(TestMessageLevel.Informational, $"{Constants.RunnerName} is about to run the host.");
            _host.Run();
            frameworkHandle?.SendMessage(TestMessageLevel.Informational, $"{Constants.RunnerName} is finished.");
        }
        catch (Exception ex)
        {
            frameworkHandle?.SendMessage(TestMessageLevel.Error, ex.GetType().FullName);
            frameworkHandle?.SendMessage(TestMessageLevel.Error, ex.Message);
            throw;
        }
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
                services.AddSingletonFromOtherService<IDiscoveryContext, IRunContext>();
            }

            if (frameworkHandle is not null)
            {
                services.AddSingleton<IFrameworkHandle>(frameworkHandle);
                services.AddSingletonFromOtherService<IMessageLogger, IFrameworkHandle>();
                services.AddSingletonFromOtherService<ITestExecutionRecorder, IFrameworkHandle>();
            }

            // nHolistic services
            services.AddSingleton<ITestRunScopeFactory, TestRunScopeFactory>();
            services.AddSingleton<ITestRunContextProvider, TestRunContextProvider>();
            services.AddScoped<ITestRunContext>(provider => provider.GetRequiredService<ITestRunContextProvider>().GetContext());
            services.AddSingleton<IProxyFactory, ProxyFactory>();
            services.AddSingleton<ISynchronizationService, SynchronizationService>();
            services.AddSingleton<IFixtureStorage, FixtureStorage>();
            services.AddSingletonFromOtherService<IFixtureProvider, IFixtureStorage>();
            services.AddSingleton<Lazy<IFixtureProvider>>(
                provider => new Lazy<IFixtureProvider>(provider.GetRequiredService<IFixtureStorage>));
            services.AddSingleton<IFixturesProcessingService, FixturesProcessingService>();
            services.AddSingleton<ITypeActivator, TypeActivator>();
            services.AddSingleton<IRunDirectoryProvider, VisualStudioRunDirectoryProvider>();
            services.AddSingleton<ITestCasesMapper, TestCasesMapper>();
            services.AddSingleton<IResultAttachmentsService, ResultAttachmentsService>();
            services.AddSingleton<IExecutionContextBuilder, ExecutionContextBuilder>();
            services.AddSingleton<ITestExecutor, TestExecutor>();
            services.AddSingleton<ITestDiscoverer, TestDiscoverer>();
            services.AddSingleton<IVisualStudioTestDiscoverer, VisualStudioTestDiscoverer>();
            services.AddSingleton<TestCasesProviderFromDiscoverer>();
            services.AddSingletonFromOtherService<ITestCasesProvider, TestCasesProviderFromDiscoverer>();
            services.AddSingletonFromOtherService<ITestCasesRegister, TestCasesProviderFromDiscoverer>();
            services.AddHostedService(provider => provider.GetRequiredService<IVisualStudioTestDiscoverer>());
            services.AddHostedService<VisualStudioTestExecutor>();
            //services.AddHostedService<TestHostedService>();

            // MediatR registrations and overrides
            services.AddMediatR(config => config.RegisterServicesFromAssembly(typeof(VsTestRunner).Assembly));
            services.AddSingleton<LogNotificationHandler>();
            services.AddSingleton<TestCaseNotificationForRunnerHandler>();
            services.RemoveAll<INotificationHandler<LogNotification>>();
            services.RemoveAll<INotificationHandler<TestCaseDiscoveredNotification>>();
            services.AddSingletonFromOtherService<INotificationHandler<LogNotification>, LogNotificationHandler>();
            services.AddSingletonFromOtherService<INotificationHandler<TestCaseDiscoveredNotification>, TestCaseNotificationForRunnerHandler>();
        });

        _host = builder.Build();
        _host.Run();
        frameworkHandle?.SendMessage(TestMessageLevel.Informational, $"{Constants.RunnerName} is finished.");
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
