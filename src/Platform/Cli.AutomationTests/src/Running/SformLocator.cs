using System.IO.Abstractions;
using System.Reflection;

namespace _42.Platform.Cli.AutomationTests.Running;

// Which sform the tests run and how it starts. Cli:Path wins: a 42.Platform.Cli.dll (run as "dotnet <dll>") or an
// executable (an installed sform, a published apphost). Without it, the build output of the referenced
// src/Platform/Cli/src of the same configuration, which MSBuild records in [assembly: AssemblyMetadata("SformPath", …)]
// when it builds the tests (RecordSformPath in the project file), so nothing guesses bin/<Configuration>.
public sealed class SformLocator
{
    public const string MetadataKey = "SformPath";

    private SformLocator(string fileName, IReadOnlyList<string> prefix, string sformPath, string source)
    {
        FileName = fileName;
        Prefix = prefix;
        SformPath = sformPath;
        Source = source;
    }

    // The program to start: the dotnet host for a dll, else the executable.
    public string FileName { get; }

    // The arguments before sform's own: the dll for the dotnet host, nothing for an executable.
    public IReadOnlyList<string> Prefix { get; }

    // The dll or the executable.
    public string SformPath { get; }

    // Where the path came from, for messages: "Cli:Path" or "the build of src/Platform/Cli/src".
    public string Source { get; }

    // The path MSBuild recorded, null when the assembly has none (e.g. a build that did not reference the CLI).
    public static string? RecordedPath { get; } = typeof(SformLocator).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == MetadataKey)?.Value;

    // Resolves the sform to run; throws an InvalidOperationException that says what to fix.
    public static SformLocator Resolve(string? configuredPath, IFileSystem? fileSystem = null)
    {
        fileSystem ??= new FileSystem();
        var (path, source) = string.IsNullOrWhiteSpace(configuredPath)
            ? (RecordedPath, "the build of src/Platform/Cli/src")
            : (fileSystem.Path.GetFullPath(configuredPath.Trim(), AppContext.BaseDirectory), "Cli:Path");

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                "The path of sform is unknown: the test assembly records none (build the tests with dotnet build, which builds src/Platform/Cli/src too), and Cli:Path is not set.");
        }

        if (!fileSystem.File.Exists(path))
        {
            throw new InvalidOperationException(source == "Cli:Path"
                ? $"Cli:Path '{path}' does not exist; set it to 42.Platform.Cli.dll or an sform executable, or remove it to run the build of src/Platform/Cli/src."
                : $"sform is not built at '{path}'; build the tests (dotnet build src/Platform/Cli.AutomationTests/src), which builds src/Platform/Cli/src too, or set Cli:Path.");
        }

        return path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? new SformLocator(DotnetHost.Resolve(fileSystem), [path], path, source)
            : new SformLocator(path, [], path, source);
    }

    // Another program in sform's place, e.g. a shell for the self tests of the runner's timeout.
    public static SformLocator Custom(string fileName, params string[] prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(prefix);
        return new SformLocator(fileName, prefix, fileName, "a program of the test");
    }

    // The error of Resolve, null when sform can be found; for the settings' validation.
    public static string? Check(string? configuredPath, IFileSystem? fileSystem = null)
    {
        try
        {
            Resolve(configuredPath, fileSystem);
            return null;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }
}
