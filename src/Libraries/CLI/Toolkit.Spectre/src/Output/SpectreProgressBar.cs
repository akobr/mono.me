using Spectre.Console;

namespace _42.CLI.Toolkit.Output;

internal sealed class SpectreProgressBar : IProgressBar
{
    private readonly ProgressTask _task;
    private readonly SpectreExtendedConsole _owner;
    private bool _disposed;

    internal SpectreProgressBar(ProgressTask task, SpectreExtendedConsole owner)
    {
        _task = task;
        _owner = owner;
    }

    public void Tick(int value, string? message = null)
    {
        if (message is not null)
        {
            _task.Description = message;
        }

        _task.Value = value;
    }

    public IProgressBar Spawn(string message, int maxTicks = 100)
    {
        return _owner.StartProgressBar(message, maxTicks);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _task.Value = _task.MaxValue; // mark as complete
        _owner.OnProgressBarDisposed();
    }
}
