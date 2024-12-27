using System.Reflection;
using MediatR;
using Microsoft.Extensions.Hosting;

namespace _42.nHolistic.Runner.VisualStudio;

public class VisualStudioTestDiscoverer(
    ISourcesProvider sourcesProvider,
    ITestDiscoverer discoverer,
    IPublisher publisher,
    IHostApplicationLifetime appLifetime)
    : IVisualStudioTestDiscoverer
{
    private CancellationTokenSource? _cancellationSource;
    private Task? _task;

    public bool IsSecondaryService { get; set; }

    public Task? DiscoveringProcess => _task;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _task = Task.Run(() => DiscoverTests(_cancellationSource.Token), _cancellationSource.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_task is null)
        {
            return;
        }

        _cancellationSource?.CancelAsync();

        try
        {
            await _task;
        }
        catch (TaskCanceledException)
        {
            await publisher.Publish(
                new LogNotification { Message = "The discovering process has been canceled." },
                CancellationToken.None);
        }
    }

    private async Task DiscoverTests(CancellationToken cancellationToken)
    {
        if (!IsSecondaryService)
        {
            await publisher.Publish(
                new LogNotification { Message = "Initialization done." },
                CancellationToken.None);
        }

        var sources = sourcesProvider.GetSources().ToArray();

        await publisher.Publish(
            new LogNotification { Message = $"Number of sources to search: {sources.Length}" },
            CancellationToken.None);

        try
        {
            var tasks = sources
                .Select(source => Task.Run(() => DiscoverTestsInSource(source, cancellationToken), cancellationToken))
                .ToArray();

            await Task.WhenAll(tasks);

            cancellationToken.ThrowIfCancellationRequested();

            await publisher.Publish(
                new LogNotification { Message = "The discovering process ended." },
                CancellationToken.None);

            if (!IsSecondaryService)
            {
                appLifetime.StopApplication();
            }
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // TODO: [P2] Log the unhandled exception
            throw;
        }
    }

    private async Task DiscoverTestsInSource(string assemblyFileNameCanBeWithoutAbsolutePath, CancellationToken cancellationToken)
    {
        var assemblyFileName = Path.GetFullPath(assemblyFileNameCanBeWithoutAbsolutePath);
        var fileNameOnly = Path.GetFileName(assemblyFileName);
        var assembly = Assembly.LoadFile(assemblyFileName);

        cancellationToken.ThrowIfCancellationRequested();

        // Silently ignore anything which doesn't look like a test project, because reporting it just throws
        // lots of warnings into the test output window as Test Explorer asks you to enumerate tests for every
        // assembly you build in your solution, not just the ones with references to this runner.
        if (!assembly.GetReferencedAssemblies().Any(name =>
                string.Equals(name.Name, "42.nHolistic.Abstractions", StringComparison.OrdinalIgnoreCase)))
        {
            await publisher.Publish(
                new LogNotification { Message = $"Skipped: {fileNameOnly}" },
                CancellationToken.None);
            return;
        }

        await publisher.Publish(
            new LogNotification { Message = $"Discovering: {fileNameOnly}" },
            CancellationToken.None);

        discoverer.DiscoverTests(assembly, assemblyFileNameCanBeWithoutAbsolutePath);

        await publisher.Publish(
            new LogNotification { Message = $"Discovered: {fileNameOnly}" },
            CancellationToken.None);
    }
}
