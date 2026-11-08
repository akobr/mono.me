using System.IO.Abstractions;

namespace _42.Platform.Cli.AutomationTests.Running.Processes;

// Whether this machine runs sform in a pseudo terminal (SformRunOptions.Terminal, mode T of the catalogue): Windows 10
// 1809 and later (ConPTY) and Linux with util-linux script. A test in a terminal ([Terminal]) skips with Reason elsewhere.
public static class TerminalSupport
{
    // Null when supported, else why not.
    public static string? Reason(IFileSystem? fileSystem = null)
    {
        fileSystem ??= new FileSystem();
        if (OperatingSystem.IsWindows())
        {
            return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) ? null : "A pseudo terminal needs Windows 10 1809 or later (ConPTY).";
        }

        if (OperatingSystem.IsLinux())
        {
            return fileSystem.File.Exists(ScriptChild.ScriptPath) || fileSystem.File.Exists("/bin/script")
                ? null
                : "A pseudo terminal on Linux needs util-linux script (/usr/bin/script); install util-linux (or bsdutils).";
        }

        return $"A pseudo terminal is not supported on {System.Runtime.InteropServices.RuntimeInformation.OSDescription}: only Windows (ConPTY) and Linux (util-linux script).";
    }

    public static bool IsSupported(IFileSystem? fileSystem = null)
    {
        return Reason(fileSystem) is null;
    }
}
