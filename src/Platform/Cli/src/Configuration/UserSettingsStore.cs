using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Security;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Configuration;

public sealed class UserSettingsStore : IUserSettingsStore
{
    // About five seconds for another sform run to finish its change.
    private const int LOCK_ATTEMPTS = 50;

    private readonly IFileSystem _fileSystem;
    private readonly IReadOnlyDictionary<string, string> _legacyFilePaths;

    // The paths of CliPaths: sform.settings.json in the user directory, the legacy files in the state directory (both
    // SFORM_HOME when it is set).
    public UserSettingsStore(IFileSystem fileSystem, CliPaths paths)
        : this(fileSystem, paths.UserSettingsFile, paths.LegacySettingsFiles)
    {
    }

    internal UserSettingsStore(IFileSystem fileSystem, string filePath, IReadOnlyDictionary<string, string> legacyFilePaths)
    {
        _fileSystem = fileSystem;
        _legacyFilePaths = legacyFilePaths;
        FilePath = filePath;
    }

    public string FilePath { get; }

    // The pause between two attempts to take the lock; shorter in the tests.
    internal TimeSpan LockRetryDelay { get; init; } = TimeSpan.FromMilliseconds(100);

    public JObject Load()
    {
        string content;

        try
        {
            if (!_fileSystem.File.Exists(FilePath))
            {
                return new JObject();
            }

            content = _fileSystem.File.ReadAllText(FilePath);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            // A link to dotfiles that are not there yet: no settings, and the first change creates the file it points to.
            return new JObject();
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            throw new UserSettingsException($"The settings file {FilePath} cannot be read: {exception.Message}", isDamaged: false, exception);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return new JObject();
        }

        JToken token;

        try
        {
            token = Parse(content);
        }
        catch (JsonReaderException exception)
        {
            throw Damaged(exception.Message, exception);
        }

        if (token is not JObject settings)
        {
            throw Damaged("it is no JSON object.");
        }

        foreach (var section in UserSettingsSchema.SectionNames)
        {
            if (UserSettingsSchema.GetValue(settings, section) is { Type: not (JTokenType.Object or JTokenType.Null) })
            {
                throw Damaged($"'{section}' is no JSON object.");
            }
        }

        return settings;
    }

    public JObject? LoadLegacy(string section)
    {
        return ReadLegacy(section).Values;
    }

    public UserSettingsSnapshot Read()
    {
        return UserSettingsSnapshot.From(Load(), LoadLegacy);
    }

    public void Update(string section, IReadOnlyDictionary<string, JToken?> changes)
    {
        // One change at a time across sform runs: a second run would write over the moved legacy sections.
        using var fileLock = AcquireLock();
        var settings = Load();
        var migrated = new List<string>();

        // The first change moves every legacy section that the settings file does not have yet, so both old files go.
        foreach (var name in UserSettingsSchema.SectionNames)
        {
            if (UserSettingsSchema.GetValue(settings, name) is JObject || ReadLegacy(name) is not { Values: { } legacy } file)
            {
                continue;
            }

            SetProperty(settings, name, legacy);

            // A file with more than its section (written by hand) stays; the settings file replaces its section anyway.
            if (file.HoldsOnlyItsSection)
            {
                migrated.Add(_legacyFilePaths[name]);
            }
        }

        var target = UserSettingsSchema.GetValue(settings, section) as JObject ?? new JObject();

        foreach (var (name, value) in changes)
        {
            if (value is null || value.Type == JTokenType.Null)
            {
                RemoveProperty(target, name);
            }
            else
            {
                SetProperty(target, name, value.DeepClone());
            }
        }

        // An empty section stays while a legacy file of it exists, so the legacy value does not show through again.
        if (target.HasValues || LegacyFileExists(section))
        {
            SetProperty(settings, section, target);
        }
        else
        {
            RemoveProperty(settings, section);
        }

        Write(settings);

        foreach (var path in migrated)
        {
            TryDelete(path);
        }

        // The legacy file of an emptied section is gone after all: the empty section no longer has anything to hide.
        if (!target.HasValues && UserSettingsSchema.GetValue(settings, section) is not null && !LegacyFileExists(section))
        {
            RemoveProperty(settings, section);
            Write(settings);
        }
    }

    // Dates stay text: a view or a command that looks like a date is kept as written.
    private static JToken Parse(string content)
    {
        using var reader = new JsonTextReader(new StringReader(content)) { DateParseHandling = DateParseHandling.None };
        var token = JToken.ReadFrom(reader, new JsonLoadSettings { CommentHandling = CommentHandling.Ignore });

        while (reader.Read())
        {
            if (reader.TokenType != JsonToken.Comment)
            {
                throw new JsonReaderException($"Additional text after the JSON content, line {reader.LineNumber}, position {reader.LinePosition}.");
            }
        }

        return token;
    }

    // Replaces the property whatever the case of its name, in its place.
    private static void SetProperty(JObject target, string name, JToken value)
    {
        var existing = target.Properties().Where(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();

        if (existing.Count == 1 && existing[0].Name == name)
        {
            existing[0].Value = value;
            return;
        }

        if (existing.Count > 0)
        {
            existing[0].AddBeforeSelf(new JProperty(name, value));
        }
        else
        {
            target.Add(name, value);
        }

        foreach (var property in existing)
        {
            property.Remove();
        }
    }

    private static void RemoveProperty(JObject target, string name)
    {
        foreach (var property in target.Properties().Where(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            property.Remove();
        }
    }

    private static bool IsFileFailure(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or NotSupportedException or SecurityException;
    }

    private UserSettingsException Damaged(string reason, Exception? innerException = null)
    {
        return new UserSettingsException($"The settings file {FilePath} is damaged: {reason}", isDamaged: true, innerException);
    }

    private (JObject? Values, bool HoldsOnlyItsSection) ReadLegacy(string section)
    {
        if (!_legacyFilePaths.TryGetValue(section, out var path))
        {
            return (null, false);
        }

        try
        {
            if (!_fileSystem.File.Exists(path)
                || Parse(_fileSystem.File.ReadAllText(path)) is not JObject root
                || UserSettingsSchema.GetValue(root, section) is not JObject raw)
            {
                return (null, false);
            }

            var normalized = UserSettingsSchema.Normalize(section, raw);
            var holdsOnlyItsSection = root.Properties().All(property => string.Equals(property.Name, section, StringComparison.OrdinalIgnoreCase));
            return (normalized.HasValues ? normalized : null, holdsOnlyItsSection);
        }
        catch (Exception exception) when (exception is JsonReaderException || IsFileFailure(exception))
        {
            // A damaged legacy file is left alone, as the configuration of earlier versions could not use it either.
            return (null, false);
        }
    }

    // An exclusive handle on sform.settings.json.lock, deleted when it is closed, as the MSAL cache of the folder does.
    private IDisposable AcquireLock()
    {
        var lockPath = $"{FilePath}.lock";

        try
        {
            var directory = _fileSystem.Path.GetDirectoryName(FilePath);

            if (!string.IsNullOrEmpty(directory))
            {
                _fileSystem.Directory.CreateDirectory(directory);
            }
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            throw new UserSettingsException($"The settings file {FilePath} cannot be written: {exception.Message}", isDamaged: false, exception);
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return _fileSystem.FileStream.New(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (Exception exception) when (attempt < LOCK_ATTEMPTS && IsHeldByAnother(exception, lockPath))
            {
                Thread.Sleep(LockRetryDelay);
            }
            catch (Exception exception) when (IsHeldByAnother(exception, lockPath))
            {
                throw new UserSettingsException($"The settings file {FilePath} is being changed by another sform run, try it again: {exception.Message}", isDamaged: false, exception);
            }
            catch (Exception exception) when (IsFileFailure(exception))
            {
                // A read-only or full disk: no retry helps.
                throw new UserSettingsException($"The settings file {FilePath} cannot be written: {exception.Message}", isDamaged: false, exception);
            }
        }
    }

    // Another process holds the lock: a sharing or lock violation on Windows, EWOULDBLOCK of the flock on Linux (11) and
    // macOS (35). On Windows a lock file that its last holder deleted is refused until a scanner lets go of it.
    private bool IsHeldByAnother(Exception exception, string lockPath)
    {
        return exception switch
        {
            IOException io => (io.HResult & 0xFFFF) is 32 or 33 or 11 or 35,
            UnauthorizedAccessException => LockFileExists(lockPath),
            _ => false,
        };
    }

    private bool LockFileExists(string lockPath)
    {
        try
        {
            return _fileSystem.File.Exists(lockPath);
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            return false;
        }
    }

    // A settings file that is a link (to dotfiles kept elsewhere) stays a link: the file it points to is replaced.
    private string GetWritePath()
    {
        // Followed link by link, so a link to a file that does not exist yet (dotfiles linked before they are written)
        // is followed too; a relative target is relative to the folder of its link. A path that is still a link (a loop,
        // or a link that cannot be read) is never written over: the change fails instead.
        const int maxLinks = 40;
        var path = FilePath;

        try
        {
            for (var hop = 0; _fileSystem.FileInfo.New(path).LinkTarget is { } target; hop++)
            {
                if (hop == maxLinks)
                {
                    throw new UserSettingsException($"The settings file {FilePath} cannot be written: it is a chain of more than {maxLinks} links.", isDamaged: false);
                }

                path = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(_fileSystem.Path.GetDirectoryName(path) ?? string.Empty, target));
            }

            return path;
        }
        catch (Exception exception) when (exception is ArgumentException || IsFileFailure(exception))
        {
            throw new UserSettingsException($"The settings file {FilePath} cannot be written: its link {path} cannot be followed: {exception.Message}", isDamaged: false, exception);
        }
    }

    private bool LegacyFileExists(string section)
    {
        try
        {
            return _legacyFilePaths.TryGetValue(section, out var path) && _fileSystem.File.Exists(path);
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            return false;
        }
    }

    // Into a temporary file in the same folder, then over the settings file, so a write never leaves half a file.
    private void Write(JObject settings)
    {
        var path = GetWritePath();
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            var directory = _fileSystem.Path.GetDirectoryName(path);

            if (path != FilePath && !string.IsNullOrEmpty(directory))
            {
                _fileSystem.Directory.CreateDirectory(directory);
            }

            _fileSystem.File.WriteAllText(temporaryPath, settings.ToString(Formatting.Indented) + Environment.NewLine);
            _fileSystem.File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            TryDelete(temporaryPath);
            throw new UserSettingsException($"The settings file {FilePath} cannot be written: {exception.Message}", isDamaged: false, exception);
        }
    }

    // A legacy file in a read-only install folder stays; the settings file replaces its section from now on.
    private void TryDelete(string path)
    {
        try
        {
            if (_fileSystem.File.Exists(path))
            {
                _fileSystem.File.Delete(path);
            }
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            // Ignored: see above.
        }
    }
}
