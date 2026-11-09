using System.IO.Abstractions;

namespace _42.Platform.Cli.AutomationTests.Running;

// The dotnet host that runs a 42.Platform.Cli.dll, as a full path: CreateProcess of a pseudo console searches the PATH of
// the tests' process, not of the environment it hands over, so the host is resolved here. In order: DOTNET_HOST_PATH
// (set by dotnet test and dotnet run for the processes they start), the current process when it is the host, the host
// under DOTNET_ROOT (and %ProgramFiles%\dotnet on Windows), the PATH, and "dotnet" as the last resort.
public static class DotnetHost
{
    public static string Resolve(IFileSystem? fileSystem = null)
    {
        fileSystem ??= new FileSystem();
        var name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

        if (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } hostPath && fileSystem.File.Exists(hostPath))
        {
            return hostPath;
        }

        if (Environment.ProcessPath is { } current && string.Equals(fileSystem.Path.GetFileName(current), name, StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        var candidates = new List<string>();
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } root)
        {
            candidates.Add(fileSystem.Path.Combine(root, name));
        }

        if (OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("ProgramFiles") is { Length: > 0 } programFiles)
        {
            candidates.Add(fileSystem.Path.Combine(programFiles, "dotnet", name));
        }

        candidates.AddRange((Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(fileSystem.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => fileSystem.Path.Combine(directory.Trim('"'), name)));

        return candidates.FirstOrDefault(fileSystem.File.Exists) ?? "dotnet";
    }
}
