using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Abstractions;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Configuring.Caching;

// The last applied server document of each identity, on disk, for a start while the platform is unreachable. One file
// per identity, written through a unique temporary file and a move, so several processes can share the directory.
// Every IO failure is logged and changes nothing else: the next applied document tries again.
internal sealed partial class LastKnownGoodCache
{
    private const int FormatVersion = 1;
    private const string EntryExtension = ".cache";
    private const string TemporaryExtension = ".tmp";

    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode EntryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode SharedWriteModes = UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;

    private static readonly TimeSpan TemporaryFileMaxAge = TimeSpan.FromHours(1);

    // Outside the protected blob, so a reader tells a protected file from a plain one before it unprotects anything.
    private static readonly byte[] ProtectedMarker = "2S-STORYTELLER-CACHE-PROTECTED/1\n"u8.ToArray();

    // The same reading as the SDK's: date-like strings of the content stay strings.
    private static readonly JsonSerializerSettings DocumentSettings = new() { DateParseHandling = DateParseHandling.None };

    private readonly string _directory;
    private readonly IFileSystem _fileSystem;
    private readonly IStorytellerCacheProtector? _protector;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly JsonSerializer _serializer = JsonSerializer.Create(DocumentSettings);
    private readonly ConcurrentDictionary<string, byte> _skipsLogged = new(StringComparer.Ordinal);
    private readonly Lock _initialization = new();
    private bool _isInitialized;

    public LastKnownGoodCache(string directory, IFileSystem fileSystem, IStorytellerCacheProtector? protector, TimeProvider time, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _directory = directory;
        _fileSystem = fileSystem;
        _protector = protector;
        _time = time;
        _logger = logger;
    }

    public bool IsProtected => _protector is not null;

    public string GetPath(CacheIdentity identity)
    {
        return _fileSystem.Path.Combine(_directory, identity.FileName + EntryExtension);
    }

    // The entry of the identity; null when there is none, or it cannot be read, does not parse, cannot be unprotected,
    // names another identity, or is plain while a protector is set (or the other way round).
    public CacheEntry? Read(CacheIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var path = GetPath(identity);
        byte[] content;

        try
        {
            if (!_fileSystem.Directory.Exists(_directory))
            {
                return null;
            }

            Initialize();

            if (!_fileSystem.File.Exists(path))
            {
                return null;
            }

            content = ReadShared(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "The cache entry {Path} of {Identity} could not be read; it is ignored.", path, identity);
            return null;
        }

        return Parse(identity, path, content);
    }

    // After a successful apply of a 200. With a protector every entry is protected; without one, a response that may
    // not be stored deletes the identity's plain entry instead (RFC 9111, section 5.2.2.5).
    public void Save(CacheIdentity identity, Configuration document, string? etag, string? cacheControl)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(document);

        if (ForbidsPlainEntry(identity, cacheControl))
        {
            SkipPlainEntry(identity, cacheControl);
            return;
        }

        byte[] content;

        try
        {
            content = Serialize(identity, document, etag, cacheControl);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A protector of the application may throw anything; the applied document stays.
            _logger.LogWarning(exception, "The cache entry of {Identity} could not be prepared; it is not written.", identity);
            return;
        }

        Write(identity, content);
    }

    // After a 304: a response that may not be stored deletes the identity's plain entry too. Returns whether it did.
    public bool Revalidated(CacheIdentity identity, string? cacheControl)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (!ForbidsPlainEntry(identity, cacheControl))
        {
            return false;
        }

        SkipPlainEntry(identity, cacheControl);
        return true;
    }

    // Without a protector, an answer that may not be stored deletes the plain entry instead of writing one.
    public bool ForbidsPlainEntry(CacheIdentity identity, string? cacheControl)
    {
        return _protector is null && !CacheControlDirectives.AllowsPlainEntry(identity.Document.Mode, cacheControl);
    }

    // Once per identity, when a logger takes it: the first answer usually comes at the startup, before the container's
    // logger factory is handed over, and the hand-over reports it then (StorytellerConfigurationEngine.UseLoggerFactory).
    public void ReportNotCached(CacheIdentity identity, string? cacheControl)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (ForbidsPlainEntry(identity, cacheControl)
            && _logger.IsEnabled(LogLevel.Information)
            && _skipsLogged.TryAdd(identity.Text, 0))
        {
            _logger.LogInformation(
                "The configuration {Identity} is not cached on disk: the platform answered Cache-Control: {CacheControl}, and no cache protector is set.",
                identity,
                cacheControl ?? "(none)");
        }
    }

    // {file name}.{32 hex}.tmp: the temporary files of this cache, never anything else in the directory.
    [GeneratedRegex("^[0-9a-f]{32}\\.[0-9a-f]{32}\\.tmp$", RegexOptions.CultureInvariant)]
    private static partial Regex TemporaryFileName();

    private CacheEntry? Parse(CacheIdentity identity, string path, byte[] content)
    {
        var isProtected = content.AsSpan().StartsWith(ProtectedMarker);

        // No downgrade: with a protector, a plain file could be anybody's.
        if (isProtected != IsProtected)
        {
            _logger.LogWarning(
                isProtected
                    ? "The cache entry {Path} of {Identity} is protected, but no cache protector is set; it is ignored."
                    : "The cache entry {Path} of {Identity} is not protected, but a cache protector is set; it is ignored.",
                path,
                identity);
            return null;
        }

        byte[] json;

        if (isProtected)
        {
            try
            {
                json = _protector!.Unprotect(content[ProtectedMarker.Length..], identity.Text);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "The cache entry {Path} of {Identity} could not be unprotected; it is ignored.", path, identity);
                return null;
            }
        }
        else
        {
            json = content;
        }

        CacheEntry? entry;

        try
        {
            entry = Deserialize(json, isProtected);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The file is anybody's who can write the directory: whatever it makes the parser throw means corrupt.
            _logger.LogWarning(exception, "The cache entry {Path} of {Identity} does not parse; it is ignored.", path, identity);
            return null;
        }

        if (entry is null)
        {
            _logger.LogWarning("The cache entry {Path} of {Identity} is not an entry of this cache; it is ignored.", path, identity);
            return null;
        }

        if (!string.Equals(entry.Identity, identity.Text, StringComparison.Ordinal))
        {
            _logger.LogWarning("The cache entry {Path} belongs to another identity than {Identity}; it is ignored.", path, identity);
            return null;
        }

        return entry;
    }

    private byte[] Serialize(CacheIdentity identity, Configuration document, string? etag, string? cacheControl)
    {
        var entry = new JObject
        {
            ["format"] = FormatVersion,
            ["identity"] = identity.Text,
            ["etag"] = etag,
            ["storedAt"] = _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture),
            ["cacheControl"] = cacheControl,
            ["document"] = JObject.FromObject(document, _serializer),
        };

        var plain = Encoding.UTF8.GetBytes(entry.ToString(Formatting.None));
        return _protector is null
            ? plain
            : [.. ProtectedMarker, .. _protector.Protect(plain, identity.Text)];
    }

    private CacheEntry? Deserialize(byte[] json, bool isProtected)
    {
        var text = new UTF8Encoding(false, true).GetString(json);
        using var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None };

        // The format is matched without a conversion, which would overflow on a number that is not an Int32.
        if (JToken.ReadFrom(reader) is not JObject root
            || root["format"] is not JValue { Type: JTokenType.Integer, Value: long format }
            || format != FormatVersion
            || root.Value<string>("identity") is not { } identity
            || root["document"] is not JObject document
            || root.Value<string>("storedAt") is not { } storedAt)
        {
            return null;
        }

        return new CacheEntry(
            identity,
            root.Value<string>("etag"),
            DateTimeOffset.Parse(storedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            root.Value<string>("cacheControl"),
            document.ToObject<Configuration>(_serializer) ?? throw new FormatException("The document of the entry is empty."),
            isProtected);
    }

    private void SkipPlainEntry(CacheIdentity identity, string? cacheControl)
    {
        ReportNotCached(identity, cacheControl);

        var path = GetPath(identity);

        try
        {
            // A protected entry belongs to a process with a protector that shares the directory.
            if (_fileSystem.File.Exists(path) && !IsProtectedFile(path))
            {
                _fileSystem.File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "The cache entry {Path} of {Identity} could not be deleted.", path, identity);
        }
    }

    private void Write(CacheIdentity identity, byte[] content)
    {
        var path = GetPath(identity);
        string? temporary = null;

        try
        {
            Initialize(createDirectory: true);
            temporary = _fileSystem.Path.Combine(_directory, $"{identity.FileName}.{Guid.NewGuid():N}{TemporaryExtension}");

            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
            };

            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = EntryMode;
            }

            using (var stream = _fileSystem.FileStream.New(temporary, options))
            {
                stream.Write(content);
            }

            _fileSystem.File.Move(temporary, path, overwrite: true);
            temporary = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "The cache entry {Path} of {Identity} could not be written; the next applied document tries again.", path, identity);
        }
        finally
        {
            if (temporary is not null)
            {
                TryDelete(temporary);
            }
        }
    }

    private byte[] ReadShared(string path)
    {
        // Another process may replace or delete the file meanwhile.
        using var stream = _fileSystem.FileStream.New(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private bool IsProtectedFile(string path)
    {
        using var stream = _fileSystem.FileStream.New(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var head = new byte[ProtectedMarker.Length];
        return stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) == head.Length
            && head.AsSpan().SequenceEqual(ProtectedMarker);
    }

    // A write creates the directory whenever it is missing, also after someone removed it. Once per cache, on the first
    // use of the directory: the mode check and the removal of temporary files that a crashed writer left. A failure is
    // retried on the next use.
    private void Initialize(bool createDirectory = false)
    {
        lock (_initialization)
        {
            var exists = _fileSystem.Directory.Exists(_directory);

            if (!exists)
            {
                if (!createDirectory)
                {
                    return;
                }

                CreateDirectory();
            }

            if (_isInitialized)
            {
                return;
            }

            // A directory this cache created is private already.
            if (exists)
            {
                WarnWhenShared();
            }

            RemoveStaleTemporaryFiles();
            _isInitialized = true;
        }
    }

    private void CreateDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            // The directory inherits its ACL; a per-user location keeps it private.
            _fileSystem.Directory.CreateDirectory(_directory);
            return;
        }

        _fileSystem.Directory.CreateDirectory(_directory, DirectoryMode);
    }

    private void WarnWhenShared()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        UnixFileMode mode;

        try
        {
            mode = _fileSystem.File.GetUnixFileMode(_directory);
        }
        catch (NotSupportedException)
        {
            // A file system without Unix modes.
            return;
        }

        if ((mode & SharedWriteModes) != 0)
        {
            _logger.LogWarning(
                "The cache directory {Directory} is writable by its group or by others, who could replace its entries; make it private (mode 0700).",
                _directory);
        }
    }

    private void RemoveStaleTemporaryFiles()
    {
        var now = _time.GetUtcNow().UtcDateTime;

        foreach (var file in _fileSystem.Directory.EnumerateFiles(_directory, "*" + TemporaryExtension))
        {
            if (!TemporaryFileName().IsMatch(_fileSystem.Path.GetFileName(file)))
            {
                continue;
            }

            try
            {
                if (now - _fileSystem.File.GetLastWriteTimeUtc(file) > TemporaryFileMaxAge)
                {
                    _fileSystem.File.Delete(file);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Another process may still write it.
            }
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            _fileSystem.File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(exception, "The temporary cache file {Path} could not be deleted.", path);
        }
    }
}
