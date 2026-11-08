using System.IO.Abstractions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Running;

namespace _42.Platform.Cli.AutomationTests.Editing;

// The fake editor of mode E (spec §4.3) as a test uses it: src/Platform/Cli.AutomationTests/editor, built with the tests,
// whose apphost is the custom command of editor.config.json (CliTest.UseEditorAsync()). What it does in a run comes from
// the variables With(change) adds to the run's options (EditorChange), and every invocation leaves a line in its log
// (ReadLog: which file sform gave it, what was in it, what it wrote, its exit code). One With call is one run: its steps
// count from the first again, and ReadLog(options) reads the invocations of that run only:
//   var editor = await UseEditorAsync();
//   var options = editor.With(EditorChange.Replace("""{ "a": 2 }"""));
//   var result = await Sform(options, InProject("story", "config", "edit", key, "-y"));
//   var opened = editor.ReadLog(options).Single();   // sform opened the editor once in that run, with the stored content
// sform appends the quoted path of the file to edit to the custom command and splits the command at its first space
// (EditorService.ParseCustomCommand), so the apphost's path must contain no space: when the build's has one, its output is
// copied once to <temp>/sform-automation/fake-editor/<hash>/ (on Windows the short 8.3 path of a temporary folder with a
// space). sform waits for the editor, and the editor inherits sform's environment (built from scratch by the runner,
// which passes the machine's DOTNET_ROOT on), so With sets DOTNET_ROOT to the dotnet the tests use when the machine has
// none and that dotnet is a real installation (links followed, host/fxr present); otherwise the apphost's own search
// (the global installation) applies.
public sealed class FakeEditor
{
    public const string MetadataKey = "EditorPath";
    public const string ModeVariable = "CLI_EDITOR_MODE";
    public const string FileVariable = "CLI_EDITOR_FILE";
    public const string LogVariable = "CLI_EDITOR_LOG";
    public const string RunVariable = "CLI_EDITOR_RUN";

    private static readonly Lock CopyLock = new();
    private static readonly Dictionary<string, string> Copies = new(StringComparer.Ordinal);

    private readonly IFileSystem fileSystem;
    private int runs;

    // directory: where the editor's folder goes (the test's working directory, outside the artifacts); each FakeEditor has
    // a folder of its own there, fake-editor/<id>/, with its log and the files of its changes.
    public FakeEditor(string directory, IFileSystem? fileSystem = null, string? command = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.fileSystem = fileSystem ?? new FileSystem();
        Command = command ?? ResolveCommand(this.fileSystem);
        Directory = this.fileSystem.Path.Combine(directory, "fake-editor", Guid.NewGuid().ToString("N")[..12]);
        LogPath = this.fileSystem.Path.Combine(Directory, "editor.log");
    }

    // The 42.Platform.Cli.AutomationTests.Editor.dll MSBuild recorded ([assembly: AssemblyMetadata("EditorPath", …)]).
    public static string? RecordedPath { get; } = typeof(FakeEditor).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == MetadataKey)?.Value;

    // The custom command of editor.config.json: the apphost, without a space.
    public string Command { get; }

    // The folder of this editor's log and of its changes' files.
    public string Directory { get; }

    // CLI_EDITOR_LOG of this editor's runs.
    public string LogPath { get; }

    // The apphost of the built fake editor, as sform can start it from a custom command: the build's, or a copy of the
    // build's output in a folder without a space. Throws an InvalidOperationException that says what to fix.
    public static string ResolveCommand(IFileSystem? fileSystem = null)
    {
        if (string.IsNullOrWhiteSpace(RecordedPath))
        {
            throw new InvalidOperationException(
                "The path of the fake editor is unknown: the test assembly records none; build the tests with dotnet build, which builds src/Platform/Cli.AutomationTests/editor too.");
        }

        return ResolveCommand(fileSystem ?? new FileSystem(), RecordedPath);
    }

    // The name of the run With gave these options (CLI_EDITOR_RUN, "run-<n>"); null for options With did not make.
    public static string? RunOf(SformRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Environment.TryGetValue(RunVariable, out var run) ? run : null;
    }

    // The options of one run with the change: the variables of EditorChange.ToEnvironment (a new run name, "run-<n>", so
    // the steps count from the first), DOTNET_ROOT for the apphost when needed, over the given options' variables. Reusing
    // the returned options for a second sform run continues the same run: a Sequence goes on where it stopped.
    public SformRunOptions With(EditorChange change, SformRunOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(change);

        var run = $"run-{Interlocked.Increment(ref runs)}";
        var environment = new Dictionary<string, string?>(options?.Environment ?? [], StringComparer.Ordinal);
        foreach (var (name, value) in change.ToEnvironment(Directory, LogPath, run, fileSystem))
        {
            environment[name] = value;
        }

        if (!environment.ContainsKey("DOTNET_ROOT") && RuntimeRoot(fileSystem) is { } root)
        {
            environment["DOTNET_ROOT"] = root;
        }

        return (options ?? new SformRunOptions()) with { Environment = environment };
    }

    // Every invocation so far, of every run, in order (empty when sform never opened the editor).
    public EditorLog ReadLog()
    {
        return EditorLog.Read(LogPath, fileSystem);
    }

    // The invocations of the run of these options (made by With) only: ReadLog(options).Single() after a run that opened
    // the editor once, Count, Last().
    public EditorLog ReadLog(SformRunOptions options)
    {
        return ReadLog().OfRun(options);
    }

    // The same for the editor's dll at a given path (the self tests' copy in a folder with a space).
    internal static string ResolveCommand(IFileSystem fileSystem, string editorDll)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(editorDll);

        var appHost = fileSystem.Path.ChangeExtension(editorDll, OperatingSystem.IsWindows() ? ".exe" : null);
        if (!fileSystem.File.Exists(appHost))
        {
            throw new InvalidOperationException(
                $"The fake editor is not built at '{appHost}' (its apphost next to {fileSystem.Path.GetFileName(editorDll)}); build the tests (dotnet build src/Platform/Cli.AutomationTests/src), which build src/Platform/Cli.AutomationTests/editor too.");
        }

        return appHost.Contains(' ', StringComparison.Ordinal) ? CopyWithoutSpace(fileSystem, appHost, editorDll) : appHost;
    }

    // <temp>/sform-automation/fake-editor: the parent of the copies of a build whose path has a space.
    internal static string CopiesRoot(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        return fileSystem.Path.Combine(TemporaryFolder(fileSystem), SformHomes.DirectoryName, "fake-editor");
    }

    // The DOTNET_ROOT the apphost needs in sform's environment, or null to leave it out: the machine's own DOTNET_ROOT
    // (or DOTNET_ROOT_<ARCH>), which the runner passes on, wins; else the folder of the dotnet the tests use with its links
    // followed (/usr/bin/dotnet is often a link to /usr/lib/dotnet/dotnet), when it has host/fxr (a real installation).
    // Without it the apphost searches the global installation itself, which a wrong DOTNET_ROOT would stop (exit 131).
    internal static string? RuntimeRoot(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var architecture = RuntimeInformation.ProcessArchitecture.ToString().ToUpperInvariant();
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 }
            || Environment.GetEnvironmentVariable($"DOTNET_ROOT_{architecture}") is { Length: > 0 })
        {
            return null;
        }

        var host = DotnetHost.Resolve(fileSystem);
        if (!fileSystem.Path.IsPathRooted(host))
        {
            return null;
        }

        try
        {
            var target = fileSystem.FileInfo.New(host).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? host;
            var root = fileSystem.Path.GetDirectoryName(target);
            return root is { Length: > 0 } && fileSystem.Directory.Exists(fileSystem.Path.Combine(root, "host", "fxr")) ? root : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The build's output (the apphost, its dll, .runtimeconfig.json, .deps.json and the dlls it needs) copied once per build
    // into <temp>/sform-automation/fake-editor/<hash of the path and the build time>/, a folder without a space.
    private static string CopyWithoutSpace(IFileSystem fileSystem, string appHost, string editorDll)
    {
        var temp = TemporaryFolder(fileSystem);
        if (temp.Contains(' ', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The fake editor's path '{appHost}' contains a space, and so does the temporary folder '{temp}' it would be copied to; sform splits the custom command of editor.config.json at its first space (EditorService.ParseCustomCommand). Build the repository in a folder without spaces, or point TEMP/TMPDIR to one.");
        }

        var source = fileSystem.Path.GetDirectoryName(appHost)!;
        var stamp = $"{appHost}|{fileSystem.File.GetLastWriteTimeUtc(appHost).Ticks}|{(fileSystem.File.Exists(editorDll) ? fileSystem.File.GetLastWriteTimeUtc(editorDll).Ticks : 0)}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamp)))[..12].ToLowerInvariant();
        var target = fileSystem.Path.Combine(CopiesRoot(fileSystem), hash);
        var copied = fileSystem.Path.Combine(target, fileSystem.Path.GetFileName(appHost));

        lock (CopyLock)
        {
            if (Copies.TryGetValue(appHost, out var known) && fileSystem.File.Exists(known))
            {
                return known;
            }

            if (!fileSystem.File.Exists(copied))
            {
                var staging = $"{target}.{Guid.NewGuid().ToString("N")[..8]}";
                CopyDirectory(fileSystem, source, staging);
                try
                {
                    fileSystem.Directory.Move(staging, target);
                }
                catch (IOException) when (fileSystem.File.Exists(copied))
                {
                    // another test process copied the same build first
                    fileSystem.Directory.Delete(staging, recursive: true);
                }
            }

            Copies[appHost] = copied;
            return copied;
        }
    }

    // The temporary folder, on Windows by its short (8.3) path when the long one has a space and the volume has short names.
    private static string TemporaryFolder(IFileSystem fileSystem)
    {
        var temp = fileSystem.Path.GetTempPath();
        return temp.Contains(' ', StringComparison.Ordinal) && OperatingSystem.IsWindows() && ShortPath(temp) is { } shortTemp ? shortTemp : temp;
    }

    private static void CopyDirectory(IFileSystem fileSystem, string source, string target)
    {
        fileSystem.Directory.CreateDirectory(target);
        foreach (var file in fileSystem.Directory.EnumerateFiles(source))
        {
            fileSystem.File.Copy(file, fileSystem.Path.Combine(target, fileSystem.Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in fileSystem.Directory.EnumerateDirectories(source))
        {
            CopyDirectory(fileSystem, directory, fileSystem.Path.Combine(target, fileSystem.Path.GetFileName(directory)));
        }
    }

    private static string? ShortPath(string path)
    {
        var buffer = new char[1024];
        var length = NativeMethods.GetShortPathNameW(path, buffer, (uint)buffer.Length);
        return length is > 0 and < 1024 ? new string(buffer, 0, (int)length) : null;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint GetShortPathNameW(string lpszLongPath, [Out] char[] lpszShortPath, uint cchBuffer);
    }
}
