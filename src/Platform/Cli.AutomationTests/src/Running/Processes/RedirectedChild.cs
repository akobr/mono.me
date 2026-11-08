using System.Diagnostics;
using System.IO.Abstractions;
using System.Text;

namespace _42.Platform.Cli.AutomationTests.Running.Processes;

// sform with its three standard streams redirected, as a script or a CI job runs it: a hidden console of its own on
// Windows, no terminal elsewhere. The arguments go as a list (ProcessStartInfo.ArgumentList, no shell); the environment
// is the request's alone, plus the tag of its ProcessJob.
//   - The output is decoded as the child wrote it (ConsoleEncodings): on Windows UTF-8 where it is valid and the OEM code
//     page of the hidden console elsewhere, UTF-8 on Linux; the input is written in the child console's code page.
//   - The run ends when the process ended and its output was read to the end. A process it started that outlived it and
//     inherited its streams (a browser opener, an editor gone to the background) keeps them open: after OutputGracePeriod
//     the job's processes are killed (ProcessJob), and if the streams are still open a little later, the reading stops
//     and the run ends with a note on its standard error.
//   - KillTree kills the process tree and every process of the job, also after the process itself ended.
internal sealed class RedirectedChild : ChildProcess
{
    // How long the output may stay open after the process ended before what it started is killed.
    public static readonly TimeSpan OutputGracePeriod = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan KilledGracePeriod = TimeSpan.FromSeconds(2);

    private readonly Process process;
    private readonly ProcessJob job;
    private readonly CancellationTokenSource reading = new();
    private readonly Task<int> exited;
    private int inputClosed;

    private RedirectedChild(Process process, ProcessJob job, Action<SformStream, string> sink)
    {
        this.process = process;
        this.job = job;
        var output = PumpAsync(process.StandardOutput, SformStream.StdOut, sink, reading.Token);
        var error = PumpAsync(process.StandardError, SformStream.StdErr, sink, reading.Token);
        exited = WaitAsync(output, error, sink);
    }

    public override int Id => process.Id;

    public override bool IsTerminal => false;

    public override Task<int> Exited => exited;

    public static RedirectedChild Start(StartRequest request, Action<SformStream, string> sink, IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(fileSystem);

        var console = ConsoleEncodings.ChildConsole;
        var startInfo = new ProcessStartInfo(request.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = console,
            StandardOutputEncoding = ConsoleEncodings.Utf8,
            StandardErrorEncoding = ConsoleEncodings.Utf8,
            WorkingDirectory = request.WorkingDirectory,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var job = ProcessJob.Create(fileSystem);
        startInfo.Environment.Clear();
        foreach (var (name, value) in request.Environment)
        {
            startInfo.Environment[name] = value;
        }

        startInfo.Environment[SformProcessEnvironment.ProcessTag] = job.Tag;

        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            process.Dispose();
            job.Dispose();
            throw new InvalidOperationException($"{request.FileName} did not start.");
        }

        job.Add(process);
        return new RedirectedChild(process, job, sink);
    }

    public override async Task WriteAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Volatile.Read(ref inputClosed) != 0)
        {
            throw new InvalidOperationException("The standard input of sform is closed.");
        }

        await process.StandardInput.WriteAsync(text.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    public override void CloseInput()
    {
        if (Interlocked.Exchange(ref inputClosed, 1) != 0)
        {
            return;
        }

        try
        {
            process.StandardInput.Close();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // the process ended already
        }
    }

    public override void KillTree()
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // ended meanwhile
        }

        // What it started and left running, which the tree of a parent that ended no longer shows.
        job.KillAll();
    }

    public override void Dispose()
    {
        CloseInput();
        reading.Cancel();
        process.Dispose();
        job.Dispose();
        reading.Dispose();
    }

    private static async Task PumpAsync(StreamReader reader, SformStream stream, Action<SformStream, string> sink, CancellationToken cancellationToken)
    {
        // The bytes, decoded here: on Windows a .NET child writes in its console's code page, and switches to UTF-8 for a
        // formatted payload (ConsoleEncodings).
        var decoder = new ConsoleOutputDecoder(ConsoleEncodings.ChildConsole);
        var source = reader.BaseStream;
        var buffer = new byte[4096];
        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            {
                var text = decoder.Decode(buffer.AsSpan(0, read));
                if (text.Length > 0)
                {
                    sink(stream, text);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // the pipe broke with a killed process, or the reading stopped: what arrived was delivered
        }

        var rest = decoder.Decode([], flush: true);
        if (rest.Length > 0)
        {
            sink(stream, rest);
        }
    }

    private async Task<int> WaitAsync(Task output, Task error, Action<SformStream, string> sink)
    {
        await process.WaitForExitAsync();
        var pumps = Task.WhenAll(output, error);
        if (await Task.WhenAny(pumps, Task.Delay(OutputGracePeriod)) != pumps)
        {
            // Something it started holds the streams: end it, so the pipes close and the rest of the output arrives.
            job.KillAll();
            if (await Task.WhenAny(pumps, Task.Delay(KilledGracePeriod)) != pumps)
            {
                await reading.CancelAsync();
                await Task.WhenAny(pumps, Task.Delay(KilledGracePeriod));
                sink(SformStream.StdErr, "\n[the tests stopped reading: a process sform started kept its standard streams open after sform ended]\n");
            }
        }

        return process.ExitCode;
    }
}
