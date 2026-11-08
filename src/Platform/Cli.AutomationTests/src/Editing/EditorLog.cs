using System.IO.Abstractions;
using System.Text.Json;
using _42.Platform.Cli.AutomationTests.Running;

namespace _42.Platform.Cli.AutomationTests.Editing;

// The log of the fake editor (CLI_EDITOR_LOG): one EditorInvocation per time sform opened it, in order; or those of one
// run (OfRun, FakeEditor.ReadLog(options)). A missing log is an empty one (sform never opened the editor).
public sealed class EditorLog
{
    private EditorLog(string path, IReadOnlyList<EditorInvocation> invocations, string? run)
    {
        Path = path;
        Invocations = invocations;
        Run = run;
    }

    public string Path { get; }

    public IReadOnlyList<EditorInvocation> Invocations { get; }

    // The run these invocations belong to ("run-<n>"); null for the whole log.
    public string? Run { get; }

    public int Count => Invocations.Count;

    public static EditorLog Read(string path, IFileSystem? fileSystem = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        fileSystem ??= new FileSystem();

        if (!fileSystem.File.Exists(path))
        {
            return new EditorLog(path, [], null);
        }

        var invocations = fileSystem.File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<EditorInvocation>(line, JsonDefaults.Web)
                ?? throw new InvalidOperationException($"The fake editor's log {path} has an empty line."))
            .ToList();
        return new EditorLog(path, invocations, null);
    }

    // The invocations of one run of sform (FakeEditor.With names each run "run-<n>").
    public EditorLog OfRun(string run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(run);
        return new EditorLog(Path, [.. Invocations.Where(invocation => invocation.Run == run)], run);
    }

    // The invocations of the run FakeEditor.With made these options for.
    public EditorLog OfRun(SformRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var run = FakeEditor.RunOf(options)
            ?? throw new ArgumentException($"The options name no run of the fake editor ({FakeEditor.RunVariable}); pass the options FakeEditor.With returned.", nameof(options));
        return OfRun(run);
    }

    // The only invocation; throws with the log when sform opened the editor not exactly once.
    public EditorInvocation Single()
    {
        return Invocations.Count == 1
            ? Invocations[0]
            : throw new InvalidOperationException($"sform opened the fake editor {Invocations.Count} time(s){Scope()}, expected once.{Describe()}");
    }

    public EditorInvocation Last()
    {
        return Invocations.Count > 0
            ? Invocations[^1]
            : throw new InvalidOperationException($"sform never opened the fake editor{Scope()} ({Path}).");
    }

    public override string ToString()
    {
        return $"{Invocations.Count} invocation(s) of the fake editor{Scope()}{Describe()}";
    }

    private string Scope() => Run is null ? string.Empty : $" in {Run}";

    private string Describe()
    {
        return Invocations.Count == 0
            ? string.Empty
            : "\n" + string.Join('\n', Invocations.Select(invocation => $"  {invocation}"));
    }
}
