using System.IO.Abstractions;

namespace _42.Platform.Cli.AutomationTests.Access;

// A state directory of sform (SFORM_HOME): with it sform keeps every file it reads or writes there (the default project
// access.default.json, the editor's editor.config.json, auth.discovery.json, logs/, and with SFORM_TOKEN_STORE=file the
// AuthKit session authkit.cache.plaintext and the MSAL cache), never in the user's profile or next to its build.
//   - A pool user's home, <temp>/sform-automation/<run id>/homes/<user fingerprint> (SformHomes), lives for the
//     session: the first test that leases the user signs it in through the device code, later tests reuse the session
//     (its refresh token rotates inside, so the folder is never copied), and before each test the default project and
//     the editor are removed (ResetDefaults).
//     A lease is exclusive, so two tests never share it.
//   - A fresh home ([FreshSignIn], CliTest.FreshHome()) and a machine's home are empty folders of the test's own.
public sealed class SformHome
{
    public const string AccessDefaultFileName = "access.default.json";
    public const string EditorConfigFileName = "editor.config.json";
    public const string AuthDiscoveryFileName = "auth.discovery.json";
    public const string AuthKitSessionFileName = "authkit.cache.plaintext";
    public const string MsalCacheFileName = "msal.cache.plaintext";
    public const string LogsDirectoryName = "logs";

    private readonly IFileSystem fileSystem;

    public SformHome(string path, SformHomeKind kind, IFileSystem fileSystem, string? owner = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(fileSystem);

        this.fileSystem = fileSystem;
        Path = fileSystem.Path.GetFullPath(path);
        Kind = kind;
        Owner = owner;
    }

    public string Path { get; }

    public SformHomeKind Kind { get; }

    // The pool user's fingerprint of a user's home; null otherwise.
    public string? Owner { get; }

    // Whether the device code sign-in succeeded in this home (and no run since said the session is gone).
    public bool IsSignedIn { get; internal set; }

    public string AccessDefaultFile => File(AccessDefaultFileName);

    public string EditorConfigFile => File(EditorConfigFileName);

    public string AuthKitSessionFile => File(AuthKitSessionFileName);

    public bool HasSession => fileSystem.File.Exists(AuthKitSessionFile) || fileSystem.File.Exists(File(MsalCacheFileName));

    public bool HasDefaultProject => fileSystem.File.Exists(AccessDefaultFile);

    public string File(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return fileSystem.Path.Combine(Path, name);
    }

    // Creates the folder (sform also creates it on its first write).
    public SformHome Ensure()
    {
        fileSystem.Directory.CreateDirectory(Path);
        return this;
    }

    // Removes the default project and the editor, so a test starts with neither unless it sets them.
    public void ResetDefaults()
    {
        foreach (var name in new[] { AccessDefaultFileName, EditorConfigFileName })
        {
            var file = File(name);
            if (fileSystem.File.Exists(file))
            {
                fileSystem.File.Delete(file);
            }
        }
    }

    // The files in the home, relative to it, logs/ included unless excluded.
    public IReadOnlyList<string> Files(bool includeLogs = true)
    {
        if (!fileSystem.Directory.Exists(Path))
        {
            return [];
        }

        return fileSystem.Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories)
            .Select(file => fileSystem.Path.GetRelativePath(Path, file))
            .Where(file => includeLogs || !file.StartsWith(LogsDirectoryName + fileSystem.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public string? ReadText(string name)
    {
        var file = File(name);
        return fileSystem.File.Exists(file) ? fileSystem.File.ReadAllText(file) : null;
    }

    public override string ToString()
    {
        return $"{Kind} home {Path}";
    }
}
