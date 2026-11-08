using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Configuration;
using Microsoft.Identity.Client.Extensions.Msal;

namespace _42.Platform.Cli.Authentication;

// Stores the AuthKit session with the MSAL extensions storage: DPAPI on Windows, Keychain on macOS and libsecret on
// Linux, for the directory of TokenStorage (the user's .42for.net folder, or SFORM_HOME), next to the MSAL cache. The
// DPAPI file lives in the directory; a Keychain item and a libsecret entry do not (only a placeholder file does), so with
// SFORM_HOME their account and an attribute carry TokenStorage.HomeKey, and each SFORM_HOME keeps a session of its own.
// With SFORM_TOKEN_STORE=file (TokenStorage.IsUnprotected) the storage is the library's unprotected file instead, which
// is what makes one session per SFORM_HOME work on a CI agent without a keyring.
public sealed class ProtectedTokenStore : ITokenStore
{
    // A local build without the TESTING constant keeps its session in a plain file.
    private const bool IsDebugPlaintext =
#if DEBUG && !TESTING
        true;
#else
        false;
#endif

    private readonly IFileSystem _fileSystem;
    private readonly Lazy<Storage> _storage;
    private readonly string _directory;
    private readonly string _lockFilePath;

    public ProtectedTokenStore(IFileSystem fileSystem, TokenStorage storage)
    {
        var settings = storage with { IsUnprotected = storage.IsUnprotected || IsDebugPlaintext };

        _fileSystem = fileSystem;
        _directory = settings.Directory;
        _lockFilePath = fileSystem.Path.Combine(_directory, settings.AuthKitCacheFileName + ".lockfile");
        _storage = new Lazy<Storage>(() => Storage.Create(CreateProperties(settings)));
    }

    internal ProtectedTokenStore(IFileSystem fileSystem, string directory)
        : this(fileSystem, new TokenStorage(directory, IsUnprotected: false))
    {
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

    // Without SFORM_HOME the Keychain account and the libsecret attributes keep their fixed values, so the session of the
    // user's folder survives; with SFORM_HOME both carry the HomeKey of the directory.
    internal static StorageCreationProperties CreateProperties(TokenStorage settings)
    {
        var builder = new StorageCreationPropertiesBuilder(settings.AuthKitCacheFileName, settings.Directory);

        if (settings.IsUnprotected)
        {
            return builder.WithUnprotectedFile().Build();
        }

        var homeKey = settings.HomeKey;

        return builder
            .WithMacKeyChain("net.42for.sform", homeKey is null ? "authkit" : $"authkit-{homeKey}")
            .WithLinuxKeyring(
                "net.42for.sform.authkit",
                MsalCacheHelper.LinuxKeyRingDefaultCollection,
                homeKey is null ? "sform AuthKit session" : $"sform AuthKit session ({settings.Directory})",
                new KeyValuePair<string, string>("Version", "1"),
                new KeyValuePair<string, string>("ProductGroup", homeKey is null ? "sform" : $"sform-{homeKey}"))
            .Build();
    }
}
