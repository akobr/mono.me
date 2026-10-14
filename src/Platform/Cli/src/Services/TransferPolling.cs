using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace _42.Platform.Cli.Services;

/// <summary>
/// Waits for an export or an import of configurations, which the server runs in the background: polls every second,
/// then every two and three seconds, until the job ends or the timeout runs out.
/// </summary>
public class TransferPolling
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(3),
    ];

    /// <summary>
    /// Reads the job until <paramref name="isDone"/> holds.
    /// </summary>
    /// <returns>The last read state, and whether the job ended before the timeout.</returns>
    public async Task<(T Job, bool IsDone)> WaitAsync<T>(Func<Task<T>> read, Func<T, bool> isDone, TimeSpan timeout, Action<T>? onRead = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var job = await read();
        var attempt = 0;

        while (!isDone(job))
        {
            if (stopwatch.Elapsed >= timeout)
            {
                return (job, false);
            }

            await DelayAsync(Delays[Math.Min(attempt++, Delays.Length - 1)]);
            job = await read();
            onRead?.Invoke(job);
        }

        return (job, true);
    }

    protected virtual Task DelayAsync(TimeSpan delay)
    {
        return Task.Delay(delay);
    }
}
