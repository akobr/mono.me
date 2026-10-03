using System;
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

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
