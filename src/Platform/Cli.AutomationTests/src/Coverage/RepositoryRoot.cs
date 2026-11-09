using System.IO.Abstractions;

namespace _42.Platform.Cli.AutomationTests.Coverage;

// The repository the tests were built in: the first folder above the test assembly that holds 42.mono.slnx; null when the
// assembly runs from a copy outside the repository (an artifact), where the catalogue is not at hand.
public static class RepositoryRoot
{
    public const string SolutionFile = "42.mono.slnx";

    public static string? Find(IFileSystem? fileSystem = null, string? start = null)
    {
        fileSystem ??= new FileSystem();
        for (var directory = start ?? AppContext.BaseDirectory; !string.IsNullOrEmpty(directory); directory = fileSystem.Path.GetDirectoryName(directory))
        {
            if (fileSystem.File.Exists(fileSystem.Path.Combine(directory, SolutionFile)))
            {
                return directory;
            }
        }

        return null;
    }
}
