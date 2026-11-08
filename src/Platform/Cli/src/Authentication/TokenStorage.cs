using System;
using System.Security.Cryptography;
using System.Text;

namespace _42.Platform.Cli.Authentication;

// Where and how the user's sign-in is kept: the AuthKit session (ProtectedTokenStore) and the Entra ID MSAL cache.
// Protected: DPAPI on Windows, Keychain on macOS, libsecret on Linux. Unprotected (SFORM_TOKEN_STORE=file with
// SFORM_HOME): plain files that only the owner may read, through the MSAL extensions' own unprotected storage.
// IsHome: the directory is SFORM_HOME, so the protected AuthKit entry of Keychain and libsecret, which do not live in
// the directory, is named after it (HomeKey).
public sealed record TokenStorage(string Directory, bool IsUnprotected, bool IsHome = false)
{
    public const string AUTHKIT_CACHE_FILE = "authkit.cache";
    public const string MSAL_CACHE_FILE = "msal.cache";
    public const string PLAINTEXT_SUFFIX = ".plaintext";

    public string AuthKitCacheFileName => IsUnprotected ? AUTHKIT_CACHE_FILE + PLAINTEXT_SUFFIX : AUTHKIT_CACHE_FILE;

    public string MsalCacheFileName => IsUnprotected ? MSAL_CACHE_FILE + PLAINTEXT_SUFFIX : MSAL_CACHE_FILE;

    // Null without SFORM_HOME (the entry keeps its fixed name, so an existing session stays); with SFORM_HOME the first
    // 16 hex digits of the SHA-256 of the directory, so each SFORM_HOME has an entry, and a session, of its own.
    public string? HomeKey => IsHome
        ? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Directory)))[..16]
        : null;
}
