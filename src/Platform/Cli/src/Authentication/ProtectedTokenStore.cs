using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client.Extensions.Msal;

namespace _42.Platform.Cli.Authentication;

// Stores the AuthKit session with the MSAL extensions storage: DPAPI on Windows,
// Keychain on macOS and libsecret on Linux, in the same ~/.42for.net directory as the MSAL cache.
public sealed class ProtectedTokenStore : ITokenStore
{
    private const string CacheFileName =
#if DEBUG && !TESTING
        "authkit.cache.plaintext";
#else
        "authkit.cache";
#endif

    private readonly IFileSystem _fileSystem;
    private readonly Lazy<Storage> _storage;
    private readonly string _directory;
    private readonly string _lockFilePath;

    public ProtectedTokenStore(IFileSystem fileSystem)
        : this(fileSystem, fileSystem.Path.Combine(MsalCacheHelper.UserRootDirectory, ".42for.net"))
    {
    }

    internal ProtectedTokenStore(IFileSystem fileSystem, string directory)
    {
        _fileSystem = fileSystem;
        _directory = directory;
        _lockFilePath = fileSystem.Path.Combine(_directory, CacheFileName + ".lockfile");
        _storage = new Lazy<Storage>(() => CreateStorage(_directory));
    }

    public Task<AuthKitSession?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var data = _storage.Value.ReadData();

        if (data.Length == 0)
        {
            return Task.FromResult<AuthKitSession?>(null);
        }

        try
        {
            return Task.FromResult(JsonSerializer.Deserialize<AuthKitSession>(data));
        }
        catch (JsonException)
        {
            // A damaged store means signing in again, not a crash.
            return Task.FromResult<AuthKitSession?>(null);
        }
    }

    public Task WriteAsync(AuthKitSession session, CancellationToken cancellationToken = default)
    {
        _storage.Value.WriteData(JsonSerializer.SerializeToUtf8Bytes(session));
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _storage.Value.Clear(ignoreExceptions: true);
        return Task.CompletedTask;
    }

    public Task<IDisposable> LockAsync(CancellationToken cancellationToken = default)
    {
        _fileSystem.Directory.CreateDirectory(_directory);

        // CrossPlatLock blocks while another process holds the file; wait up to about a minute.
        return Task.Run<IDisposable>(() => new CrossPlatLock(_lockFilePath, 100, 600), cancellationToken);
    }

    private static Storage CreateStorage(string directory)
    {
        var properties = new StorageCreationPropertiesBuilder(CacheFileName, directory)
#if DEBUG && !TESTING
            .WithUnprotectedFile()
#else
            .WithMacKeyChain("net.42for.sform", "authkit")
            .WithLinuxKeyring(
                "net.42for.sform.authkit",
                MsalCacheHelper.LinuxKeyRingDefaultCollection,
                "sform AuthKit session",
                new KeyValuePair<string, string>("Version", "1"),
                new KeyValuePair<string, string>("ProductGroup", "sform"))
#endif
            .Build();

        return Storage.Create(properties);
    }
}
