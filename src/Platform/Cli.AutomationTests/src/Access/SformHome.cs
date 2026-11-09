using System.IO.Abstractions;

namespace _42.Platform.Cli.AutomationTests.Access;

// A state directory of sform (SFORM_HOME): with it sform keeps every file it reads or writes there (the default project
// and the editor in sform.settings.json, auth.discovery.json, logs/, and with SFORM_TOKEN_STORE=file the AuthKit
// session authkit.cache.plaintext and the MSAL cache), never in the user's profile or next to its build.
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

    // The user's settings since 424-218: the sections access (the default project) and editor, in camelCase
    // (organizationName, projectName, viewName, editorType, customCommand). sform still reads the legacy
    // access.default.json and editor.config.json of the home while this file has no section of its own.
    public const string SettingsFileName = "sform.settings.json";
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

    public string SettingsFile => File(SettingsFileName);

    public string AuthKitSessionFile => File(AuthKitSessionFileName);

    public bool HasSession => fileSystem.File.Exists(AuthKitSessionFile) || fileSystem.File.Exists(File(MsalCacheFileName));

    public bool HasDefaultProject => AccessSetting("projectName") is not null;

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

    // Removes the default project and the editor, so a test starts with neither unless it sets them: the settings file
    // and the legacy files.
    public void ResetDefaults()
    {
        Delete(SettingsFileName);
        Delete(AccessDefaultFileName);
        Delete(EditorConfigFileName);
    }

    // Removes the default project only (the access section of the settings, and the legacy access.default.json), e.g.
    // the one account points create sets; the editor stays.
    public void ResetDefaultProject()
    {
        RemoveSettingsSection("access");
        Delete(AccessDefaultFileName);
    }

    // The settings file as JSON, or null without one (or with one that is not a JSON object).
    public System.Text.Json.Nodes.JsonObject? ReadSettings()
    {
        var text = ReadText(SettingsFileName);

        try
        {
            return text is null ? null : System.Text.Json.Nodes.JsonNode.Parse(text, documentOptions: new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true }) as System.Text.Json.Nodes.JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // A property of the access section as sform reads it: the settings file when it has the section, else the legacy
    // access.default.json; names match in any case (organizationName, projectName, viewName).
    public string? AccessSetting(string name)
    {
        var section = SectionOf(ReadSettings(), "access");

        if (section is null)
        {
            var legacy = ReadText(AccessDefaultFileName);
            section = legacy is null ? null : SectionOf(System.Text.Json.Nodes.JsonNode.Parse(legacy) as System.Text.Json.Nodes.JsonObject, "access");
        }

        return section?.FirstOrDefault(property => string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase)).Value?.GetValue<string>();
    }

    // Sets a section of the settings file (creating the file), as sform account settings would.
    public void WriteSettingsSection(string section, System.Text.Json.Nodes.JsonObject value)
    {
        Ensure();
        var settings = ReadSettings() ?? [];
        settings[section] = value;
        fileSystem.File.WriteAllText(SettingsFile, settings.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), new System.Text.UTF8Encoding(false));
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

    private void Delete(string name)
    {
        var file = File(name);
        if (fileSystem.File.Exists(file))
        {
            fileSystem.File.Delete(file);
        }
    }

    private static System.Text.Json.Nodes.JsonObject? SectionOf(System.Text.Json.Nodes.JsonObject? settings, string section)
    {
        return settings?.FirstOrDefault(property => string.Equals(property.Key, section, StringComparison.OrdinalIgnoreCase)).Value as System.Text.Json.Nodes.JsonObject;
    }

    private void RemoveSettingsSection(string section)
    {
        var settings = ReadSettings();
        var name = settings?.Select(property => property.Key).FirstOrDefault(key => string.Equals(key, section, StringComparison.OrdinalIgnoreCase));

        if (settings is null || name is null)
        {
            return;
        }

        settings.Remove(name);
        fileSystem.File.WriteAllText(SettingsFile, settings.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), new System.Text.UTF8Encoding(false));
    }
}
