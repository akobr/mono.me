using System.Collections.Concurrent;
using System.IO.Abstractions;
using _42.Testing.UI.Automation.Runtime;
using _42.Testing.UI.Automation.Runtime.Users;

namespace _42.Platform.Cli.AutomationTests.Access;

// The homes (SFORM_HOME) and working directories of the running session, all under <temp>/sform-automation/<run id>/,
// outside Execution:ArtifactsPath, so a job that uploads the artifacts (or a zip of TestResults shared for a failure)
// never carries them: a home holds a plain session (SFORM_TOKEN_STORE=file), a working directory the files sform wrote
// (the .pfx of machine create -o, a GITHUB_ENV file with resolved secrets, a kv export).
//   - homes/<user fingerprint>/: a pool user's, one per user, created on first use and kept for the session (the target
//     check signs its user in there too, so the user's first test signs in no more);
//   - homes/tests/<id>/: an empty home of one test (a fresh sign-in, a machine), removed when the test ends;
//   - work/<test>-<id>/: the working directory of one test, removed when the test ends (kept when it did not pass, or
//     with Cli:KeepWorkingDirectories; the test then attaches the list of its files).
// Keyed by the session's run id, so an IDE that runs several sessions in one process starts each from scratch. When the
// process exits, the session files (*.plaintext) of every home are deleted; the logs stay for a look after the run.
public static class SformHomes
{
    public const string DirectoryName = "sform-automation";

    private static readonly ConcurrentDictionary<(string RunId, string User), SformHome> Homes = new();
    private static readonly ConcurrentDictionary<string, IFileSystem> Roots = new(StringComparer.Ordinal);
    private static int exitHook;

    // <temp>/sform-automation/<run id>: the folder of the run's homes and working directories.
    public static string RunRoot(IFileSystem fileSystem, string runId)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var root = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), DirectoryName, runId);
        Roots.TryAdd(root, fileSystem);
        if (Interlocked.Exchange(ref exitHook, 1) == 0)
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => RemoveSessions();
        }

        return root;
    }

    // The user's home of the session; its sign-in state is kept with it.
    public static SformHome ForUser(AutomationSession session, TestUser user)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(user);

        return Homes.GetOrAdd(
            (session.RunId, user.Fingerprint),
            static (key, fileSystem) => new SformHome(
                fileSystem.Path.Combine(RunRoot(fileSystem, key.RunId), "homes", key.User),
                SformHomeKind.User,
                fileSystem,
                key.User).Ensure(),
            session.FileSystem);
    }

    // A new empty home of one test; delete it with Remove when the test ends.
    public static SformHome NewForTest(AutomationSession session, SformHomeKind kind)
    {
        ArgumentNullException.ThrowIfNull(session);

        var fileSystem = session.FileSystem;
        var name = $"{(kind == SformHomeKind.Machine ? "machine" : "fresh")}-{Guid.NewGuid():N}"[..20];
        var path = fileSystem.Path.Combine(RunRoot(fileSystem, session.RunId), "homes", "tests", name);
        return new SformHome(path, kind, fileSystem).Ensure();
    }

    // A new working directory of one test (created), named after it; delete it with RemoveDirectory when the test ends.
    public static string NewWorkingDirectory(AutomationSession session, string name)
    {
        ArgumentNullException.ThrowIfNull(session);

        var fileSystem = session.FileSystem;
        var safe = new string((name ?? "test").Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '_').ToArray());
        safe = safe.Length > 60 ? safe[..60] : safe;
        var path = fileSystem.Path.Combine(RunRoot(fileSystem, session.RunId), "work", $"{safe}-{Guid.NewGuid().ToString("N")[..8]}");
        fileSystem.Directory.CreateDirectory(path);
        return path;
    }

    // Deletes a test's home; never throws (a file sform still holds stays until the process exits).
    public static void Remove(SformHome home, IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(fileSystem);
        if (home.Kind == SformHomeKind.User)
        {
            return;
        }

        RemoveDirectory(home.Path, fileSystem);
    }

    // Deletes a folder of the run (a test's home or working directory); never throws.
    public static void RemoveDirectory(string path, IFileSystem fileSystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(fileSystem);
        try
        {
            if (fileSystem.Directory.Exists(path))
            {
                fileSystem.Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // a file a process still holds; the session files go at the exit, the rest with the temporary folder
        }
    }

    // Deletes the session files under every root of this process; registered for the process's exit.
    public static void RemoveSessions()
    {
        foreach (var (root, fileSystem) in Roots)
        {
            try
            {
                if (!fileSystem.Directory.Exists(root))
                {
                    continue;
                }

                foreach (var file in fileSystem.Directory.EnumerateFiles(root, "*.plaintext", SearchOption.AllDirectories).ToArray())
                {
                    fileSystem.File.Delete(file);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // best effort at the exit
            }
        }
    }
}
