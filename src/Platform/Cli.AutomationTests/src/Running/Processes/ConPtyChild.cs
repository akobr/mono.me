using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace _42.Platform.Cli.AutomationTests.Running.Processes;

// sform in a Windows pseudo console (ConPTY, Windows 10 1809 and later): a console of its own whose screen arrives as VT
// text on a pipe and whose keyboard is the VT input the test writes to another pipe, so Sharprompt's prompts run as in a
// terminal. No package: CreatePseudoConsole and CreateProcess with PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE through P/Invoke.
//
// The child gets STARTF_USESTDHANDLES with no handles: a test host whose own standard streams are redirected (dotnet test)
// would otherwise hand them down, and the child would write there instead of the pseudo console. The output pipe is read
// all the time on a thread of its own (a pseudo console stops its client when nobody reads); once the process ended,
// ClosePseudoConsole flushes the last frame and ends the pipe, which completes Exited.
[SupportedOSPlatform("windows")]
internal sealed class ConPtyChild : ChildProcess
{
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const int StartfUseStdHandles = 0x00000100;
    private static readonly IntPtr ProcThreadAttributePseudoConsole = 0x00020016;

    private readonly IntPtr pseudoConsole;
    private readonly SafeProcessHandle processHandle;
    private readonly FileStream input;
    private readonly Task<int> exited;
    private readonly object gate = new();
    private int closed;

    private ConPtyChild(int id, IntPtr pseudoConsole, SafeProcessHandle processHandle, FileStream input, FileStream output, Action<SformStream, string> sink)
    {
        Id = id;
        this.pseudoConsole = pseudoConsole;
        this.processHandle = processHandle;
        this.input = input;
        var reading = Task.Factory.StartNew(() => Pump(output, sink), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        exited = WaitAsync(reading);
    }

    public override int Id { get; }

    public override bool IsTerminal => true;

    public override Task<int> Exited => exited;

    public static ConPtyChild Start(StartRequest request, Action<SformStream, string> sink)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            throw new PlatformNotSupportedException("A pseudo console needs Windows 10 1809 (build 17763) or later.");
        }

        SafeFileHandle? inputRead = null;
        SafeFileHandle? inputWrite = null;
        SafeFileHandle? outputRead = null;
        SafeFileHandle? outputWrite = null;
        var pseudoConsole = IntPtr.Zero;
        var attributes = IntPtr.Zero;
        var environment = IntPtr.Zero;
        try
        {
            Check(Native.CreatePipe(out inputRead, out inputWrite, IntPtr.Zero, 0), "CreatePipe");
            Check(Native.CreatePipe(out outputRead, out outputWrite, IntPtr.Zero, 0), "CreatePipe");

            var size = new Native.Coord { X = (short)Math.Clamp(request.Columns, 20, short.MaxValue), Y = (short)Math.Clamp(request.Rows, 5, short.MaxValue) };
            var result = Native.CreatePseudoConsole(size, inputRead, outputWrite, 0, out pseudoConsole);
            if (result != 0)
            {
                throw new Win32Exception(result, $"CreatePseudoConsole failed with 0x{result:X8}.");
            }

            // The pseudo console holds its own copies of its ends of the pipes.
            inputRead.Dispose();
            outputWrite.Dispose();

            var attributesSize = IntPtr.Zero;
            Native.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributesSize);
            attributes = Marshal.AllocHGlobal(attributesSize);
            Check(Native.InitializeProcThreadAttributeList(attributes, 1, 0, ref attributesSize), "InitializeProcThreadAttributeList");
            Check(
                Native.UpdateProcThreadAttribute(attributes, 0, ProcThreadAttributePseudoConsole, pseudoConsole, IntPtr.Size, IntPtr.Zero, IntPtr.Zero),
                "UpdateProcThreadAttribute");

            var startupInfo = new Native.StartupInfoEx
            {
                StartupInfo = new Native.StartupInfo
                {
                    Cb = Marshal.SizeOf<Native.StartupInfoEx>(),
                    DwFlags = StartfUseStdHandles,
                },
                LpAttributeList = attributes,
            };

            environment = Marshal.StringToHGlobalUni(EnvironmentBlock(request.Environment));

            // CreateProcessW may write into the command line, so it gets a buffer of its own, ended by a NUL.
            var commandLine = (CommandLines.Windows([request.FileName, .. request.Arguments]) + "\0").ToCharArray();
            Check(
                Native.CreateProcessW(
                    request.FileName,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    ExtendedStartupInfoPresent | CreateUnicodeEnvironment,
                    environment,
                    request.WorkingDirectory,
                    ref startupInfo,
                    out var processInformation),
                $"CreateProcess of {request.FileName}");

            Native.CloseHandle(processInformation.HThread);
            var input = new FileStream(inputWrite, FileAccess.Write, 1, isAsync: false);
            var output = new FileStream(outputRead, FileAccess.Read, 1, isAsync: false);
            var child = new ConPtyChild(processInformation.DwProcessId, pseudoConsole, new SafeProcessHandle(processInformation.HProcess, ownsHandle: true), input, output, sink);
            pseudoConsole = IntPtr.Zero;
            inputWrite = null;
            outputRead = null;
            return child;
        }
        finally
        {
            if (attributes != IntPtr.Zero)
            {
                Native.DeleteProcThreadAttributeList(attributes);
                Marshal.FreeHGlobal(attributes);
            }

            if (environment != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(environment);
            }

            if (pseudoConsole != IntPtr.Zero)
            {
                Native.ClosePseudoConsole(pseudoConsole);
            }

            inputRead?.Dispose();
            inputWrite?.Dispose();
            outputRead?.Dispose();
            outputWrite?.Dispose();
        }
    }

    public override async Task WriteAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = Encoding.UTF8.GetBytes(text);

        // A synchronous pipe: written off the caller's thread so a cancellation can give up waiting.
        await Task.Run(
            () =>
            {
                lock (gate)
                {
                    input.Write(bytes, 0, bytes.Length);
                    input.Flush();
                }
            },
            cancellationToken);
    }

    // The keyboard stays until the end; a pseudo console has no end of input other than its close.
    public override void CloseInput()
    {
    }

    public override void KillTree()
    {
        try
        {
            using var process = Process.GetProcessById(Id);
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            Native.TerminateProcess(processHandle, 1);
        }
    }

    public override void Dispose()
    {
        ClosePseudoConsole();
        input.Dispose();
        processHandle.Dispose();
    }

    // name=value\0 … \0, sorted by name without case as Windows expects.
    private static string EnvironmentBlock(IReadOnlyDictionary<string, string> environment)
    {
        var builder = new StringBuilder();
        foreach (var (name, value) in environment.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(name).Append('=').Append(value).Append('\0');
        }

        return builder.Append('\0').ToString();
    }

    private static void Check(bool succeeded, string what)
    {
        if (!succeeded)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"{what} failed.");
        }
    }

    private static void Pump(FileStream output, Action<SformStream, string> sink)
    {
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[8192];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        try
        {
            int read;
            while ((read = output.Read(bytes, 0, bytes.Length)) > 0)
            {
                var count = decoder.GetChars(bytes, 0, read, chars, 0, flush: false);
                if (count > 0)
                {
                    sink(SformStream.Terminal, new string(chars, 0, count));
                }
            }
        }
        catch (IOException)
        {
            // the pipe ended (ERROR_BROKEN_PIPE) with the pseudo console
        }
        finally
        {
            output.Dispose();
        }
    }

    private async Task<int> WaitAsync(Task reading)
    {
        await WaitForProcessAsync();
        Native.GetExitCodeProcess(processHandle, out var exitCode);

        // The last frame is flushed and the output pipe ends with the pseudo console.
        await Task.Run(ClosePseudoConsole);
        await reading;
        return exitCode;
    }

    private Task WaitForProcessAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = new ManualResetEvent(false) { SafeWaitHandle = new SafeWaitHandle(processHandle.DangerousGetHandle(), ownsHandle: false) };
        RegisteredWaitHandle? registration = null;
        registration = ThreadPool.RegisterWaitForSingleObject(
            wait,
            (_, _) =>
            {
                registration?.Unregister(null);
                wait.Dispose();
                completion.TrySetResult();
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: true);
        return completion.Task;
    }

    private void ClosePseudoConsole()
    {
        if (Interlocked.Exchange(ref closed, 1) == 0)
        {
            Native.ClosePseudoConsole(pseudoConsole);
        }
    }

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern int CreatePseudoConsole(Coord size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPc);

        [DllImport("kernel32.dll")]
        public static extern void ClosePseudoConsole(IntPtr hPc);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, int nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateProcessW(
            string? lpApplicationName,
            char[] lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string? lpCurrentDirectory,
            ref StartupInfoEx lpStartupInfo,
            out ProcessInformation lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetExitCodeProcess(SafeProcessHandle hProcess, out int lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateProcess(SafeProcessHandle hProcess, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        public struct Coord
        {
            public short X;
            public short Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct StartupInfo
        {
            public int Cb;
            public IntPtr LpReserved;
            public IntPtr LpDesktop;
            public IntPtr LpTitle;
            public int DwX;
            public int DwY;
            public int DwXSize;
            public int DwYSize;
            public int DwXCountChars;
            public int DwYCountChars;
            public int DwFillAttribute;
            public int DwFlags;
            public short WShowWindow;
            public short CbReserved2;
            public IntPtr LpReserved2;
            public IntPtr HStdInput;
            public IntPtr HStdOutput;
            public IntPtr HStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct StartupInfoEx
        {
            public StartupInfo StartupInfo;
            public IntPtr LpAttributeList;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ProcessInformation
        {
            public IntPtr HProcess;
            public IntPtr HThread;
            public int DwProcessId;
            public int DwThreadId;
        }
    }
}
