using MediatR;
using Microsoft.Extensions.Hosting;

namespace _42.nHolistic.Runner.VisualStudio;

public class VisualStudioTestExecutor(
    ITestCasesProvider testCasesProvider,
    ITestExecutor executor,
    IPublisher publisher,
    IHostApplicationLifetime appLifetime) : IVisualStudioTestExecutor
{
    private CancellationTokenSource _cancellationSource = new();
    private Task? _task;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        appLifetime.ApplicationStarted.Register(OnApplicationStarted);
        return Task.CompletedTask;
    }

    private void OnApplicationStarted()
    {
       _task = Task.Run(() => ExecuteTestCases(_cancellationSource.Token), _cancellationSource.Token);
    }

    private async Task ExecuteTestCases(CancellationToken cancellationToken)
    {
        await publisher.Publish(
            new LogNotification { Message = "Initialization done." },
            CancellationToken.None);

        try
        {
            var testCases = await testCasesProvider.GetTestCasesAsync();
            await executor.ExecuteTestCasesAsync(testCases, cancellationToken);

            await publisher.Publish(
                new LogNotification { Message = "The execution process ended." },
                CancellationToken.None);
        }
        finally
        {
            appLifetime.StopApplication();
        }
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
                new LogNotification { Message = "The execution process has been canceled." },
                CancellationToken.None);
        }
    }
}
