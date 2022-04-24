using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace _42.Monorepo.Repo.Generator;

internal class Executor
{
    public static async Task ExecuteScriptAsync(string script, string? workingDirectory)
    {
        Console.WriteLine($"> {script}");

        var arguments = script.Replace("\"", "\"\"\"");

        ProcessStartInfo startInfo = new("powershell", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };

        var process = Process.Start(startInfo);

        if (process is null
            || process.HasExited)
        {
            return;
        }

        await process.WaitForExitAsync();
    }
}
