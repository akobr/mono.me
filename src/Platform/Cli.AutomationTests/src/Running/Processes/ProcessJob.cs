using System.ComponentModel;
using System.Diagnostics;
using System.IO.Abstractions;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace _42.Platform.Cli.AutomationTests.Running.Processes;

// Everything a started process started, also what outlives it: Process.Kill(entireProcessTree) walks the tree from a
// living parent, so it misses a child that sform started and left running (a browser opener, an editor that went to the
// background), which still holds sform's standard streams. ProcessJob finds those too:
//   - Windows: a job object with kill-on-close; the process joins it right after the start (sform starts nothing in its
//     first milliseconds), its descendants join with it, and KillAll terminates the job. Closing the job (Dispose) kills
//     what is left, also when the tests' process ends.
//   - Elsewhere: the process is started with the variable CLI_AUTOMATION_PROCESS_TAG=<tag>, which its descendants
//     inherit, and KillAll kills every process of /proc whose environment holds it.
// Best effort: what cannot be joined or killed (a nested job a CI agent forbids, a process of another user) stays.
internal sealed partial class ProcessJob : IDisposable
{
    private readonly IFileSystem fileSystem;
    private IntPtr job;

    private ProcessJob(string tag, IFileSystem fileSystem)
    {
        Tag = tag;
        this.fileSystem = fileSystem;
    }

    // The value of SformProcessEnvironment.ProcessTag in the process's environment.
    public string Tag { get; }

    public static ProcessJob Create(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        return new ProcessJob(Guid.NewGuid().ToString("N"), fileSystem);
    }

    // Joins the started process (Windows; elsewhere the tag of its environment is enough).
    public void Add(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (OperatingSystem.IsWindows())
        {
            AddOnWindows(process);
        }
    }

    // Kills every process of the job but the tests' own; never throws.
    public void KillAll()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (job != IntPtr.Zero)
                {
                    _ = Native.TerminateJobObject(job, 1);
                }

                return;
            }

            if (OperatingSystem.IsLinux())
            {
                KillTaggedOnLinux();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            // best effort
        }
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows() && job != IntPtr.Zero)
        {
            // kill-on-close: what is left of the job ends with it
            _ = Native.CloseHandle(job);
            job = IntPtr.Zero;
        }
    }

    [SupportedOSPlatform("windows")]
    private void AddOnWindows(Process process)
    {
        var handle = Native.CreateJobObjectW(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var limits = new Native.JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new Native.JobObjectBasicLimitInformation { LimitFlags = Native.JobObjectLimitKillOnJobClose },
        };
        var size = Marshal.SizeOf<Native.JobObjectExtendedLimitInformation>();
        if (!Native.SetInformationJobObject(handle, Native.JobObjectExtendedLimitInformationClass, ref limits, (uint)size)
            || !Native.AssignProcessToJobObject(handle, process.Handle))
        {
            _ = Native.CloseHandle(handle);
            return;
        }

        job = handle;
    }

    private void KillTaggedOnLinux()
    {
        var marker = Encoding.UTF8.GetBytes($"{SformProcessEnvironment.ProcessTag}={Tag}\0");
        var self = Environment.ProcessId;
        foreach (var directory in fileSystem.Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(fileSystem.Path.GetFileName(directory), out var pid) || pid == self)
            {
                continue;
            }

            byte[] environment;
            try
            {
                environment = fileSystem.File.ReadAllBytes(fileSystem.Path.Combine(directory, "environ"));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (environment.AsSpan().IndexOf(marker) < 0)
            {
                continue;
            }

            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill();
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // ended meanwhile
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static partial class Native
    {
        public const int JobObjectExtendedLimitInformationClass = 9;
        public const uint JobObjectLimitKillOnJobClose = 0x2000;

        [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        public static partial IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetInformationJobObject(IntPtr hJob, int jobObjectInfoClass, ref JobObjectExtendedLimitInformation lpJobObjectInfo, uint cbJobObjectInfoLength);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool TerminateJobObject(IntPtr hJob, uint uExitCode);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        public struct JobObjectBasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JobObjectExtendedLimitInformation
        {
            public JobObjectBasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }
    }
}
