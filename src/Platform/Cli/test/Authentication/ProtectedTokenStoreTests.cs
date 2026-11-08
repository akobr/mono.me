using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Text;
using System.Threading.Tasks;
using _42.Platform.Cli.Authentication;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Authentication;

// Runs the real DPAPI-backed storage in a temporary directory. macOS and Linux need a keychain
// or keyring session, so the checks only run on Windows.
public sealed class ProtectedTokenStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sform-store-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Session_RoundTripsEncryptedAndClears()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new ProtectedTokenStore(new FileSystem(), _directory);
        var session = new AuthKitSession
        {
            AccessToken = "access-secret-value",
            RefreshToken = "refresh-secret-value",
            AccessTokenExpiresAt = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero),
            User = new AuthKitSessionUser("user_01", "ada@example.com", "Ada", "Lovelace"),
            OrganizationId = "org_01",
        };

        (await store.ReadAsync()).ShouldBeNull();

        await store.WriteAsync(session);

        (await store.ReadAsync()).ShouldBe(session);
        var raw = await File.ReadAllBytesAsync(Path.Combine(_directory, "authkit.cache"));
        Encoding.UTF8.GetString(raw).ShouldNotContain("refresh-secret-value");

        await store.ClearAsync();

        (await store.ReadAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task Lock_CreatesTheLockFileAndIsReleasedOnDispose()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new ProtectedTokenStore(new FileSystem(), _directory);

        using (await store.LockAsync())
        {
            File.Exists(Path.Combine(_directory, "authkit.cache.lockfile")).ShouldBeTrue();
        }

        // A second lock succeeds once the first one is released.
        using var second = await store.LockAsync();
    }

    // SFORM_TOKEN_STORE=file: the library's unprotected file, on every OS and without a keyring, in a directory that
    // does not exist before the first sign-in (SFORM_HOME).
    [Fact]
    public async Task FileStore_KeepsTheSessionInAPlainFileOfTheDirectory()
    {
        var store = new ProtectedTokenStore(new FileSystem(), new TokenStorage(_directory, IsUnprotected: true));
        var session = new AuthKitSession
        {
            AccessToken = "access-value",
            RefreshToken = "refresh-value",
            AccessTokenExpiresAt = new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero),
            User = new AuthKitSessionUser("user_01", "ada@example.com", "Ada", "Lovelace"),
            OrganizationId = "org_01",
        };

        (await store.ReadAsync()).ShouldBeNull();

        using (await store.LockAsync())
        {
            await store.WriteAsync(session);
            File.Exists(Path.Combine(_directory, "authkit.cache.plaintext.lockfile")).ShouldBeTrue();
        }

        (await store.ReadAsync()).ShouldBe(session);
        var raw = await File.ReadAllTextAsync(Path.Combine(_directory, "authkit.cache.plaintext"));
        raw.ShouldContain("refresh-value");

        await store.ClearAsync();

        (await store.ReadAsync()).ShouldBeNull();
    }

    [Fact]
    public void TokenStorage_NamesThePlainFilesApart()
    {
        new TokenStorage(_directory, IsUnprotected: false).AuthKitCacheFileName.ShouldBe("authkit.cache");
        new TokenStorage(_directory, IsUnprotected: false).MsalCacheFileName.ShouldBe("msal.cache");
        new TokenStorage(_directory, IsUnprotected: true).AuthKitCacheFileName.ShouldBe("authkit.cache.plaintext");
        new TokenStorage(_directory, IsUnprotected: true).MsalCacheFileName.ShouldBe("msal.cache.plaintext");
    }

    // Keychain and libsecret keep the secret themselves, not in the directory: without SFORM_HOME the entry keeps its
    // fixed names (the existing session stays), with SFORM_HOME each directory gets an entry of its own.
    [Fact]
    public void ProtectedEntry_WithoutHome_KeepsItsFixedNames()
    {
        var properties = ProtectedTokenStore.CreateProperties(new TokenStorage(_directory, IsUnprotected: false));

        properties.MacKeyChainServiceName.ShouldBe("net.42for.sform");
        properties.MacKeyChainAccountName.ShouldBe("authkit");
        properties.KeyringSchemaName.ShouldBe("net.42for.sform.authkit");
        properties.KeyringSecretLabel.ShouldBe("sform AuthKit session");
        properties.KeyringAttribute1.ShouldBe(new KeyValuePair<string, string>("Version", "1"));
        properties.KeyringAttribute2.ShouldBe(new KeyValuePair<string, string>("ProductGroup", "sform"));
        properties.CacheFilePath.ShouldBe(Path.Combine(_directory, "authkit.cache"));
    }

    [Fact]
    public void ProtectedEntry_OfEachHome_IsItsOwn()
    {
        var home = Path.Combine(_directory, "a");
        var storage = new TokenStorage(home, IsUnprotected: false, IsHome: true);
        var other = ProtectedTokenStore.CreateProperties(new TokenStorage(Path.Combine(_directory, "b"), IsUnprotected: false, IsHome: true));

        var properties = ProtectedTokenStore.CreateProperties(storage);

        storage.HomeKey.ShouldNotBeNull().ShouldMatch("^[0-9a-f]{16}$");
        properties.MacKeyChainServiceName.ShouldBe("net.42for.sform");
        properties.MacKeyChainAccountName.ShouldBe($"authkit-{storage.HomeKey}");
        properties.KeyringSchemaName.ShouldBe("net.42for.sform.authkit");
        properties.KeyringAttribute2.ShouldBe(new KeyValuePair<string, string>("ProductGroup", $"sform-{storage.HomeKey}"));
        properties.KeyringSecretLabel.ShouldBe($"sform AuthKit session ({home})");
        other.MacKeyChainAccountName.ShouldNotBe(properties.MacKeyChainAccountName);
        other.KeyringAttribute2.ShouldNotBe(properties.KeyringAttribute2);
        ProtectedTokenStore.CreateProperties(storage).MacKeyChainAccountName.ShouldBe(properties.MacKeyChainAccountName);
    }

    [Fact]
    public void FileStore_UsesTheUnprotectedFile()
    {
        var properties = ProtectedTokenStore.CreateProperties(new TokenStorage(_directory, IsUnprotected: true, IsHome: true));

        properties.UseUnencryptedFallback.ShouldBeTrue();
        properties.CacheFilePath.ShouldBe(Path.Combine(_directory, "authkit.cache.plaintext"));
    }

    [Fact]
    public void HomeKey_WithoutHome_IsNull()
    {
        new TokenStorage(_directory, IsUnprotected: false).HomeKey.ShouldBeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
